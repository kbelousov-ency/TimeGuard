using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TimeGuard.Core;

namespace TimeGuard.Service;

/// <summary>Небольшой HTTP-сервис истории для родительского приложения в LAN.</summary>
public sealed class HistoryServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ILogger _log;
    private readonly GuardConfig _config;
    private readonly Func<int, IReadOnlyList<ActivityRecord>> _history;
    private readonly Func<OnlineControlCommand, OnlineControlResult>? _control;
    private readonly ReplayGuard _replay = new();
    private CancellationTokenSource? _cts;
    private TcpListener? _listener;
    private Task? _loop;

    public HistoryServer(
        ILogger log,
        GuardConfig config,
        Func<int, IReadOnlyList<ActivityRecord>> history,
        Func<OnlineControlCommand, OnlineControlResult>? control = null)
    {
        _log = log;
        _config = config;
        _history = history;
        _control = control;
    }

    public void Start(CancellationToken stopToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(stopToken);
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Any, HistoryProtocol.Port);
            _listener = listener;
            listener.Start();
            _log.LogInformation("История приложений доступна в LAN на TCP-порту {Port}.", HistoryProtocol.Port);

            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException ex)
        {
            _log.LogWarning(ex, "Не удалось открыть TCP-порт истории {Port}.", HistoryProtocol.Port);
        }
        finally
        {
            _listener?.Stop();
            _listener = null;
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        using (NetworkStream stream = client.GetStream())
        {
            try
            {
                LanHttpRequest? request;
                try
                {
                    request = await LanHttpRequest.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
                }
                catch (LanHttpException ex)
                {
                    await WriteResponseAsync(stream, ex.StatusCode, ex.Reason, "").ConfigureAwait(false);
                    return;
                }
                if (request is null)
                {
                    return;
                }

                if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteResponseAsync(stream, 405, "Method Not Allowed", "").ConfigureAwait(false);
                    return;
                }

                if (!Uri.TryCreate("http://localhost" + request.Target, UriKind.Absolute, out Uri? uri) ||
                    (uri.AbsolutePath != HistoryProtocol.Route && uri.AbsolutePath != HistoryProtocol.ControlRoute))
                {
                    await WriteResponseAsync(stream, 404, "Not Found", "").ConfigureAwait(false);
                    return;
                }

                if (!HistoryProtocol.Authenticate(
                        request, _config.GetSecret(), _config.MachineId, uri.AbsolutePath,
                        _replay, DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
                {
                    await WriteResponseAsync(stream, 401, "Unauthorized", "").ConfigureAwait(false);
                    return;
                }

                if (uri.AbsolutePath == HistoryProtocol.ControlRoute)
                {
                    if (!string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase) || _control is null)
                    {
                        await WriteResponseAsync(stream, 405, "Method Not Allowed", "").ConfigureAwait(false);
                        return;
                    }
                    OnlineControlCommand? command = JsonSerializer.Deserialize<OnlineControlCommand>(request.BodyText, Json);
                    if (command is null)
                    {
                        await WriteResponseAsync(stream, 400, "Bad Request", "").ConfigureAwait(false);
                        return;
                    }
                    OnlineControlResult result = _control(command);
                    await WriteResponseAsync(stream, 200, "OK", JsonSerializer.Serialize(result, Json), "application/json; charset=utf-8").ConfigureAwait(false);
                    return;
                }

                int limit = HistoryProtocol.MaxEntries;
                string query = uri.Query.TrimStart('?');
                string? limitValue = query
                    .Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Split('=', 2))
                    .Where(pair => pair.Length == 2 && string.Equals(pair[0], "limit", StringComparison.OrdinalIgnoreCase))
                    .Select(pair => pair[1])
                    .FirstOrDefault();
                if (int.TryParse(limitValue, out int requestedLimit))
                {
                    limit = requestedLimit;
                }
                string body = JsonSerializer.Serialize(_history(Math.Clamp(limit, 1, HistoryProtocol.MaxEntries)), Json);
                await WriteResponseAsync(stream, 200, "OK", body, "application/json; charset=utf-8").ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Ошибка запроса истории приложений.");
            }
        }
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        int statusCode,
        string reason,
        string body,
        string contentType = "text/plain; charset=utf-8")
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        string header = $"HTTP/1.1 {statusCode} {reason}\r\n" +
                        $"Content-Type: {contentType}\r\n" +
                        $"Content-Length: {bytes.Length}\r\n" +
                        "Connection: close\r\n\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes).ConfigureAwait(false);
        if (bytes.Length > 0)
        {
            await stream.WriteAsync(bytes).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            if (_loop is not null)
            {
                await _loop.ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
        }
        _cts?.Dispose();
    }
}
