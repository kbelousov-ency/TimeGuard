using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Text;
using System.Text.Json;
using TimeGuard.Core;

namespace TimeGuard.Parent;

public sealed class PendingOnlineRequest
{
    internal TaskCompletionSource<OnlineTimeResponse> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Id { get; init; } = string.Empty;
    public int Minutes { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed class ParentOnlineServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ParentConfig _config;
    private readonly Dictionary<string, PendingOnlineRequest> _pending = new();
    private readonly object _gate = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    public event Action<PendingOnlineRequest>? RequestReceived;

    public ParentOnlineServer(ParentConfig config) => _config = config;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public IReadOnlyList<PendingOnlineRequest> Pending
    {
        get { lock (_gate) return _pending.Values.ToArray(); }
    }

    public void Resolve(string id, bool accepted)
    {
        lock (_gate)
        {
            if (!_pending.Remove(id, out PendingOnlineRequest? request)) return;
            request.Completion.TrySetResult(new OnlineTimeResponse
            {
                Accepted = accepted,
                Message = accepted ? $"Запрос одобрен: +{request.Minutes} мин." : "Запрос отклонён родителем.",
                AddedMinutes = accepted ? request.Minutes : 0,
            });
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, HistoryProtocol.Port + 1);
            _listener.Start();
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = HandleAsync(client, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
        finally { _listener?.Stop(); }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using TcpClient ownedClient = client;
        using NetworkStream stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
        try
        {
            string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) return;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                string? header = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(header)) break;
                int colon = header.IndexOf(':');
                if (colon > 0) headers[header[..colon].Trim()] = header[(colon + 1)..].Trim();
            }
            string[] parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || !string.Equals(parts[0], "POST", StringComparison.OrdinalIgnoreCase) ||
                !Uri.TryCreate("http://localhost" + parts[1], UriKind.Absolute, out Uri? uri) ||
                uri.AbsolutePath != HistoryProtocol.ParentRequestRoute)
            {
                await ReplyAsync(stream, 405, "").ConfigureAwait(false);
                return;
            }
            if (!headers.TryGetValue(HistoryProtocol.MachineHeader, out string? machine) ||
                !string.Equals(machine, _config.ClientMachineId, StringComparison.Ordinal) ||
                !headers.TryGetValue(HistoryProtocol.TimestampHeader, out string? ts) || !long.TryParse(ts, out long timestamp) ||
                !headers.TryGetValue(HistoryProtocol.SignatureHeader, out string? signature) ||
                !HistoryProtocol.IsValidSignatureForRoute(Convert.FromBase64String(_config.SecretBase64), machine, timestamp, signature, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), HistoryProtocol.ParentRequestRoute))
            {
                await ReplyAsync(stream, 401, "").ConfigureAwait(false);
                return;
            }
            int length = headers.TryGetValue("Content-Length", out string? lengthText) && int.TryParse(lengthText, out int parsed) ? parsed : 0;
            char[] body = new char[Math.Clamp(length, 0, 4096)];
            int read = 0;
            while (read < body.Length)
            {
                int count = await reader.ReadAsync(body.AsMemory(read, body.Length - read), ct).ConfigureAwait(false);
                if (count == 0) break;
                read += count;
            }
            OnlineTimeRequest? requestBody = JsonSerializer.Deserialize<OnlineTimeRequest>(new string(body, 0, read), Json);
            if (requestBody is null || requestBody.Minutes is < 1 or > TimeGuardCodec.MaxMinutes)
            {
                await ReplyAsync(stream, 400, "").ConfigureAwait(false);
                return;
            }
            var pending = new PendingOnlineRequest { Id = Guid.NewGuid().ToString("N"), Minutes = requestBody.Minutes, Reason = requestBody.Reason };
            lock (_gate) _pending[pending.Id] = pending;
            RequestReceived?.Invoke(pending);
            OnlineTimeResponse response;
            Task completed = await Task.WhenAny(pending.Completion.Task, Task.Delay(TimeSpan.FromMinutes(5), ct)).ConfigureAwait(false);
            response = completed == pending.Completion.Task ? await pending.Completion.Task.ConfigureAwait(false) : new OnlineTimeResponse { Message = "Родитель не ответил вовремя." };
            lock (_gate) _pending.Remove(pending.Id);
            await ReplyAsync(stream, 200, JsonSerializer.Serialize(response, Json)).ConfigureAwait(false);
        }
        catch (Exception) { }
    }

    private static async Task ReplyAsync(NetworkStream stream, int code, string body)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} OK\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header).ConfigureAwait(false);
        await stream.WriteAsync(bytes).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();
        if (_loop is not null) await _loop.ConfigureAwait(false);
        _cts?.Dispose();
    }
}
