using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using TimeGuard.Core;

namespace TimeGuard.Service;

/// <summary>
/// Отвечает на UDP-маяки родителя. Порт может быть занят (в том числе стандартным пользователем),
/// поэтому бинд выполняется внутри цикла с повтором и никогда не бросает исключение наружу.
/// </summary>
public sealed class ParentDiscoveryListener : IAsyncDisposable
{
    internal static readonly TimeSpan BindRetryInterval = TimeSpan.FromSeconds(30);

    private readonly ILogger _log;
    private readonly byte[] _secret;
    private readonly string _machineId;
    private readonly object _gate = new();
    private UdpClient? _udp;
    private bool _disposed;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    public event Action<string>? ParentDiscovered;

    public ParentDiscoveryListener(ILogger log, byte[] secret, string machineId)
    {
        _log = log;
        _secret = secret;
        _machineId = machineId;
    }

    public void Start(CancellationToken stopToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(stopToken);
        CancellationToken ct = _cts.Token;
        _loop = Task.Run(() => RunAsync(ct));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            UdpClient? udp = await BindWithRetryAsync(ct).ConfigureAwait(false);
            if (udp is null) return;
            await ReceiveLoopAsync(udp, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // сбой обнаружения не должен влиять на учёт времени
            _log.LogWarning(ex, "Обнаружение родителя остановлено из-за ошибки.");
        }
    }

    private async Task<UdpClient?> BindWithRetryAsync(CancellationToken ct)
    {
        bool warned = false;
        while (!ct.IsCancellationRequested)
        {
            UdpClient? udp = null;
            try
            {
                udp = new UdpClient(AddressFamily.InterNetwork) { ExclusiveAddressUse = true };
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryProtocol.Port));
                DisableConnectionReset(udp);
                lock (_gate)
                {
                    if (_disposed)
                    {
                        udp.Dispose();
                        return null;
                    }
                    _udp = udp;
                }
                _log.LogInformation("Обнаружение родителя слушает UDP-порт {Port}.", DiscoveryProtocol.Port);
                return udp;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                udp?.Dispose();
                if (!warned)
                {
                    warned = true;
                    _log.LogWarning(ex,
                        "Не удалось занять UDP-порт {Port} для обнаружения родителя. Повтор каждые {Seconds} с.",
                        DiscoveryProtocol.Port, (int)BindRetryInterval.TotalSeconds);
                }
            }
            await Task.Delay(BindRetryInterval, ct).ConfigureAwait(false);
        }
        return null;
    }

    private async Task ReceiveLoopAsync(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult packet = await udp.ReceiveAsync(ct).ConfigureAwait(false);
                DiscoveryBeacon? beacon = DiscoveryProtocol.Deserialize(Encoding.UTF8.GetString(packet.Buffer));
                if (beacon is null || beacon.Kind != DiscoveryProtocol.ParentKind ||
                    !DiscoveryProtocol.IsValid(_secret, beacon, DateTimeOffset.UtcNow.ToUnixTimeSeconds())) continue;
                ParentDiscovered?.Invoke(packet.RemoteEndPoint.Address.ToString());
                var reply = new DiscoveryBeacon
                {
                    Kind = DiscoveryProtocol.ClientKind,
                    MachineId = _machineId,
                    Name = Environment.MachineName,
                    Port = HistoryProtocol.Port,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                };
                reply.Signature = DiscoveryProtocol.Sign(_secret, reply);
                byte[] bytes = Encoding.UTF8.GetBytes(DiscoveryProtocol.Serialize(reply));
                await udp.SendAsync(bytes, bytes.Length, packet.RemoteEndPoint).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { await Task.Delay(1000, ct).ConfigureAwait(false); }
        }
    }

    /// <summary>Без этого ICMP «порт недоступен» после ответа превращается в SocketException на приёме.</summary>
    private static void DisableConnectionReset(UdpClient udp)
    {
        try { udp.Client.IOControl((IOControlCode)(-1744830452), [0, 0, 0, 0], null); }
        catch (Exception) { }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _cts?.Cancel();
            lock (_gate)
            {
                _disposed = true;
                _udp?.Dispose();
            }
            if (_loop is not null) await _loop.ConfigureAwait(false);
        }
        catch (Exception) { }
        _cts?.Dispose();
    }
}
