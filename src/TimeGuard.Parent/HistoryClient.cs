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
        Uri endpoint = BuildEndpoint(host, HistoryProtocol.Route, $"limit={Math.Clamp(limit, 1, HistoryProtocol.MaxEntries)}");
        using var request = CreateRequest(HttpMethod.Get, endpoint, config, HistoryProtocol.Route, null);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Клиент вернул HTTP {(int)response.StatusCode}.");
        }

        return JsonSerializer.Deserialize<List<ActivityRecord>>(body, Json) ?? new List<ActivityRecord>();
    }

    public static async Task<OnlineControlResult> ControlAsync(
        ParentConfig config,
        string host,
        OnlineControlCommand command,
        CancellationToken cancellationToken)
    {
        Uri endpoint = BuildEndpoint(host, HistoryProtocol.ControlRoute, string.Empty);
        using var request = CreateRequest(
            HttpMethod.Post, endpoint, config, HistoryProtocol.ControlRoute, JsonSerializer.SerializeToUtf8Bytes(command, Json));
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Клиент вернул HTTP {(int)response.StatusCode}.");
        }
        return JsonSerializer.Deserialize<OnlineControlResult>(body, Json) ?? new OnlineControlResult { Message = "Пустой ответ клиента." };
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri endpoint, ParentConfig config, string route, byte[]? jsonBody)
        => HistoryProtocol.CreateSignedRequest(
            method, endpoint, Convert.FromBase64String(config.SecretBase64), config.ClientMachineId, route, jsonBody);

    private static Uri BuildEndpoint(string host, string path, string query)
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
            Path = path,
            Query = query,
        };
        return builder.Uri;
    }
}
