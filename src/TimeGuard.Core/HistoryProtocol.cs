using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace TimeGuard.Core;

/// <summary>
/// Общие параметры и подпись LAN-запросов между родителем и клиентом.
/// Подписывается строка <c>machineId\ntimestamp\nnonce\nroute\nsha256hex(body)</c>;
/// сервер дополнительно отклоняет повтор пары (timestamp, signature) через <see cref="ReplayGuard"/>.
/// </summary>
public static class HistoryProtocol
{
    public const int Port = 38765;
    public const string Route = "/timeguard/history";
    public const string ControlRoute = "/timeguard/control";
    public const string ParentRequestRoute = "/timeguard/request-time";
    public const string MachineHeader = "X-TimeGuard-Machine";
    public const string TimestampHeader = "X-TimeGuard-Timestamp";
    public const string NonceHeader = "X-TimeGuard-Nonce";
    public const string SignatureHeader = "X-TimeGuard-Signature";
    public const int MaxEntries = 500;
    public const long TimestampWindowSeconds = 300;
    public const int MaxBodyBytes = 32 * 1024;
    public const int MaxHeaderLineBytes = 8 * 1024;
    public const int MaxNonceLength = 64;

    public static string NewNonce() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>SHA-256 тела в нижнем hex; для запроса без тела — хэш пустой строки.</summary>
    public static string HashBody(ReadOnlySpan<byte> body) => Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();

    public static string Sign(
        ReadOnlySpan<byte> secret,
        string machineId,
        long timestamp,
        string nonce,
        string route,
        ReadOnlySpan<byte> body)
    {
        string payload = string.Create(CultureInfo.InvariantCulture, $"{machineId}\n{timestamp}\n{nonce}\n{route}\n{HashBody(body)}");
        return Convert.ToHexString(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static bool IsValidSignature(
        ReadOnlySpan<byte> secret,
        string machineId,
        long timestamp,
        string nonce,
        string signature,
        long nowUnixSeconds,
        string route,
        ReadOnlySpan<byte> body)
    {
        if (timestamp < nowUnixSeconds - TimestampWindowSeconds ||
            timestamp > nowUnixSeconds + TimestampWindowSeconds ||
            !IsWellFormedNonce(nonce) ||
            string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        try
        {
            byte[] expected = Convert.FromHexString(Sign(secret, machineId, timestamp, nonce, route, body));
            byte[] supplied = Convert.FromHexString(signature);
            return CryptographicOperations.FixedTimeEquals(expected, supplied);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Проверяет заголовки подписи входящего запроса против ожидаемых machineId и маршрута,
    /// затем регистрирует подпись в <paramref name="replay"/>. Повтор уже принятой подписи отклоняется.
    /// </summary>
    public static bool Authenticate(
        LanHttpRequest request,
        ReadOnlySpan<byte> secret,
        string expectedMachineId,
        string route,
        ReplayGuard replay,
        long nowUnixSeconds)
    {
        return request.Headers.TryGetValue(MachineHeader, out string? machine) &&
               string.Equals(machine, expectedMachineId, StringComparison.Ordinal) &&
               request.Headers.TryGetValue(TimestampHeader, out string? timestampText) &&
               long.TryParse(timestampText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long timestamp) &&
               request.Headers.TryGetValue(NonceHeader, out string? nonce) &&
               request.Headers.TryGetValue(SignatureHeader, out string? signature) &&
               IsValidSignature(secret, machine, timestamp, nonce, signature, nowUnixSeconds, route, request.Body) &&
               replay.TryAccept(timestamp, signature, nowUnixSeconds);
    }

    /// <summary>
    /// Создаёт подписанный запрос. Тело (если есть) кладётся в запрос теми же байтами, что подписаны.
    /// </summary>
    public static HttpRequestMessage CreateSignedRequest(
        HttpMethod method,
        Uri endpoint,
        ReadOnlySpan<byte> secret,
        string machineId,
        string route,
        byte[]? jsonBody)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string nonce = NewNonce();
        byte[] body = jsonBody ?? [];
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.TryAddWithoutValidation(MachineHeader, machineId);
        request.Headers.TryAddWithoutValidation(TimestampHeader, timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(NonceHeader, nonce);
        request.Headers.TryAddWithoutValidation(SignatureHeader, Sign(secret, machineId, timestamp, nonce, route, body));
        if (jsonBody is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }
        return request;
    }

    private static bool IsWellFormedNonce(string nonce)
        => nonce.Length is > 0 and <= MaxNonceLength && nonce.All(char.IsAsciiHexDigit);
}
