using System.Net.Http;
using System.Text.Json;
using TimeGuard.Core;

namespace TimeGuard.Parent;

public static class HistoryClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<IReadOnlyList<ActivityRecord>> FetchAsync(
        ParentConfig config,
        string host,
        int limit,
        CancellationToken cancellationToken)
    {
        Uri endpoint = BuildEndpoint(host, limit);
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.TryAddWithoutValidation(HistoryProtocol.MachineHeader, config.ClientMachineId);
        request.Headers.TryAddWithoutValidation(
            HistoryProtocol.TimestampHeader,
            timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(
            HistoryProtocol.SignatureHeader,
            HistoryProtocol.Sign(Convert.FromBase64String(config.SecretBase64), config.ClientMachineId, timestamp));

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Клиент вернул HTTP {(int)response.StatusCode}.");
        }

        return JsonSerializer.Deserialize<List<ActivityRecord>>(body, Json) ?? new List<ActivityRecord>();
    }

    private static Uri BuildEndpoint(string host, int limit)
    {
        string value = host.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Укажите имя или IP компьютера ребёнка.", nameof(host));
        }
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            if (value.Count(c => c == ':') > 1 && !value.StartsWith("[", StringComparison.Ordinal))
            {
                value = $"[{value}]";
            }
            value = "http://" + value;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Адрес компьютера указан неверно.", nameof(host));
        }

        var builder = new UriBuilder(parsed)
        {
            Port = parsed.IsDefaultPort ? HistoryProtocol.Port : parsed.Port,
            Path = HistoryProtocol.Route,
            Query = $"limit={Math.Clamp(limit, 1, HistoryProtocol.MaxEntries)}",
        };
        return builder.Uri;
    }
}
