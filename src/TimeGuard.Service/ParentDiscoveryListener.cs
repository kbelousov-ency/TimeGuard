using System.Net;
using System.Net.Sockets;
using System.Text;
using TimeGuard.Core;

namespace TimeGuard.Service;

public sealed class ParentDiscoveryListener : IAsyncDisposable
{
    private readonly byte[] _secret;
    private readonly string _machineId;
    private readonly UdpClient _udp = new(DiscoveryProtocol.Port);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    public event Action<string>? ParentDiscovered;

    public ParentDiscoveryListener(byte[] secret, string machineId)
    {
        _secret = secret;
        _machineId = machineId;
        _udp.Client.ReceiveTimeout = 2000;
    }

    public void Start(CancellationToken stopToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(stopToken);
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult packet = await _udp.ReceiveAsync(ct).ConfigureAwait(false);
                DiscoveryBeacon? beacon = DiscoveryProtocol.Deserialize(Encoding.UTF8.GetString(packet.Buffer));
                if (beacon is null || beacon.Kind != DiscoveryProtocol.ParentKind ||
                    !DiscoveryProtocol.IsValid(_secret, beacon, DateTimeOffset.UtcNow.ToUnixTimeSeconds())) continue;
                ParentDiscovered?.Invoke(packet.RemoteEndPoint.Address.ToString());
                var reply = new DiscoveryBeacon
                {
                    Kind = DiscoveryProtocol.ClientKind,
                    MachineId = _machineId,
                    Name = Environment.MachineName,
                    Port = 38765,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                };
                reply.Signature = DiscoveryProtocol.Sign(_secret, reply);
                byte[] bytes = Encoding.UTF8.GetBytes(DiscoveryProtocol.Serialize(reply));
                await _udp.SendAsync(bytes, bytes.Length, packet.RemoteEndPoint).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { await Task.Delay(1000, ct).ConfigureAwait(false); }
            catch (ObjectDisposedException) { break; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _udp.Dispose();
        if (_loop is not null) await _loop.ConfigureAwait(false);
        _cts?.Dispose();
    }
}
