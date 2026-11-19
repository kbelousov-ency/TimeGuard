using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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
    /// <summary>Адрес, с которого пришёл подписанный запрос; на него уходит команда add.</summary>
    public string ClientHost { get; init; } = string.Empty;
}

public sealed class ParentOnlineServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ParentConfig _config;
    private readonly Dictionary<string, PendingOnlineRequest> _pending = new();
    private readonly object _gate = new();
    private readonly ReplayGuard _replay = new();
    private TcpListener? _listener;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private Task? _discoveryLoop;
    public event Action<PendingOnlineRequest>? RequestReceived;
    public event Action<string>? ClientDiscovered;

    /// <summary>
    /// null — автообнаружение работает. Иначе причина, по которой UDP-порт обнаружения не удалось занять
    /// (второй экземпляр родителя, клиент на этом же ПК, чужая программа); остальное приложение при этом работает.
    /// </summary>
    public string? DiscoveryError { get; private set; }

    public ParentOnlineServer(ParentConfig config) => _config = config;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;
        _loop = Task.Run(() => AcceptLoopAsync(ct));
        try
        {
            UdpClient udp = new(DiscoveryProtocol.Port) { EnableBroadcast = true };
            _udp = udp;
            try { udp.Client.IOControl((IOControlCode)(-1744830452), [0, 0, 0, 0], null); }
            catch (Exception) { }
            // маяк и приём — независимые циклы: незавершённый ReceiveAsync не должен съедать пакеты
            _discoveryLoop = Task.WhenAll(
                Task.Run(() => DiscoveryLoopAsync(udp, ct)),
                Task.Run(() => DiscoveryReceiveLoopAsync(udp, ct)));
        }
        catch (Exception ex)
        {
            _udp?.Dispose();
            _udp = null;
            DiscoveryError = ex.Message;
        }
    }

    public IReadOnlyList<PendingOnlineRequest> Pending
    {
        get { lock (_gate) return _pending.Values.ToArray(); }
    }

    /// <summary>
    /// Решение родителя по запросу. При одобрении родитель сам отправляет подписанную команду add на клиент,
    /// и клиенту уходит Accepted=true, только если она выполнена. null — запрос уже истёк или обработан.
    /// </summary>
    public async Task<OnlineTimeResponse?> ResolveAsync(string id, bool accepted)
    {
        PendingOnlineRequest? request;
        lock (_gate)
        {
            if (!_pending.Remove(id, out request)) return null;
        }
        OnlineTimeResponse response = accepted
            ? await AddOnClientAsync(request).ConfigureAwait(false)
            : new OnlineTimeResponse { Message = "Запрос отклонён родителем." };
        request.Completion.TrySetResult(response);
        return response;
    }

    private async Task<OnlineTimeResponse> AddOnClientAsync(PendingOnlineRequest request)
    {
        string host = string.IsNullOrWhiteSpace(request.ClientHost) ? _config.ClientHost : request.ClientHost;
        try
        {
            OnlineControlResult result = await HistoryClient.ControlAsync(
                _config, host, new OnlineControlCommand { Action = "add", Minutes = request.Minutes }, CancellationToken.None).ConfigureAwait(false);
            if (!result.Accepted)
            {
                return new OnlineTimeResponse
                {
                    Message = $"Родитель одобрил запрос, но компьютер ребёнка не добавил время: {result.Message}",
                    RemainingSeconds = result.RemainingSeconds,
                };
            }
            return new OnlineTimeResponse
            {
                Accepted = true,
                Message = $"Запрос одобрен: +{request.Minutes} мин.",
                AddedMinutes = request.Minutes,
                RemainingSeconds = result.RemainingSeconds,
            };
        }
        catch (HttpRequestException ex)
        {
            return new OnlineTimeResponse { Message = $"Родитель одобрил запрос, но не смог связаться с компьютером ребёнка ({host}): {ex.Message} Время не добавлено." };
        }
        catch (Exception ex)
        {
            return new OnlineTimeResponse { Message = $"Родитель одобрил запрос, но добавление времени на компьютере ребёнка ({host}) не подтверждено: {ex.Message}" };
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

    /// <summary>Цикл маяка: раз в 5 с шлёт подписанный broadcast. Ответы читает DiscoveryReceiveLoopAsync.</summary>
    private async Task DiscoveryLoopAsync(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var beacon = new DiscoveryBeacon
                {
                    Kind = DiscoveryProtocol.ParentKind,
                    MachineId = _config.ClientMachineId,
                    Name = Environment.MachineName,
                    Port = HistoryProtocol.Port + 1,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                };
                beacon.Signature = DiscoveryProtocol.Sign(Convert.FromBase64String(_config.SecretBase64), beacon);
                byte[] bytes = Encoding.UTF8.GetBytes(DiscoveryProtocol.Serialize(beacon));
                await udp.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryProtocol.Port)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { } // сети может не быть; следующая попытка через 5 с
            catch (FormatException) { break; } // повреждённый секрет: подписать маяк нельзя

            try { await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Цикл приёма: постоянно читает сокет и принимает только подписанные ответы своего клиента.</summary>
    private async Task DiscoveryReceiveLoopAsync(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult packet = await udp.ReceiveAsync(ct).ConfigureAwait(false);
                DiscoveryBeacon? client = DiscoveryProtocol.Deserialize(Encoding.UTF8.GetString(packet.Buffer));
                if (client is not null && client.Kind == DiscoveryProtocol.ClientKind && client.MachineId == _config.ClientMachineId &&
                    DiscoveryProtocol.IsValid(Convert.FromBase64String(_config.SecretBase64), client, DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
                {
                    ClientDiscovered?.Invoke(packet.RemoteEndPoint.Address.ToString());
                }
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (FormatException) { break; }
            catch (SocketException)
            {
                try { await Task.Delay(1000, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
            catch (Exception) { } // сбой обработчика события не должен останавливать приём
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using TcpClient ownedClient = client;
        using NetworkStream stream = client.GetStream();
        try
        {
            LanHttpRequest? request;
            try
            {
                request = await LanHttpRequest.ReadAsync(stream, ct).ConfigureAwait(false);
            }
            catch (LanHttpException ex)
            {
                await ReplyAsync(stream, ex.StatusCode, "").ConfigureAwait(false);
                return;
            }
            if (request is null) return;
            if (!string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase) ||
                !Uri.TryCreate("http://localhost" + request.Target, UriKind.Absolute, out Uri? uri) ||
                uri.AbsolutePath != HistoryProtocol.ParentRequestRoute)
            {
                await ReplyAsync(stream, 405, "").ConfigureAwait(false);
                return;
            }
            if (!HistoryProtocol.Authenticate(
                    request, Convert.FromBase64String(_config.SecretBase64), _config.ClientMachineId,
                    HistoryProtocol.ParentRequestRoute, _replay, DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
            {
                await ReplyAsync(stream, 401, "").ConfigureAwait(false);
                return;
            }
            OnlineTimeRequest? requestBody;
            try { requestBody = JsonSerializer.Deserialize<OnlineTimeRequest>(request.BodyText, Json); }
            catch (JsonException) { requestBody = null; }
            if (requestBody is null || requestBody.Minutes is < 1 or > TimeGuardCodec.MaxMinutes)
            {
                await ReplyAsync(stream, 400, "").ConfigureAwait(false);
                return;
            }
            var pending = new PendingOnlineRequest
            {
                Id = Guid.NewGuid().ToString("N"),
                Minutes = requestBody.Minutes,
                Reason = requestBody.Reason,
                ClientHost = RemoteHost(client),
            };
            lock (_gate) _pending[pending.Id] = pending;
            RequestReceived?.Invoke(pending);
            Task completed = await Task.WhenAny(pending.Completion.Task, Task.Delay(TimeSpan.FromMinutes(5), ct)).ConfigureAwait(false);
            if (completed != pending.Completion.Task)
            {
                // если родитель уже нажал «Одобрить», ResolveAsync забрал запрос и досылает add — ждём его результат
                bool expired;
                lock (_gate) expired = _pending.Remove(pending.Id);
                if (expired) pending.Completion.TrySetResult(new OnlineTimeResponse { Message = "Родитель не ответил вовремя." });
            }
            OnlineTimeResponse response = await pending.Completion.Task.ConfigureAwait(false);
            await ReplyAsync(stream, 200, JsonSerializer.Serialize(response, Json)).ConfigureAwait(false);
        }
        catch (Exception) { }
    }

    private static string RemoteHost(TcpClient client)
    {
        if (client.Client.RemoteEndPoint is not IPEndPoint remote) return string.Empty;
        IPAddress address = remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address;
        return address.ToString();
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
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            _udp?.Dispose();
            if (_loop is not null) await _loop.ConfigureAwait(false);
            if (_discoveryLoop is not null) await _discoveryLoop.ConfigureAwait(false);
        }
        catch (Exception) { }
        _cts?.Dispose();
    }
}
