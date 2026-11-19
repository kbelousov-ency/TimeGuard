using System.IO;
using System.IO.Pipes;
using System.Text;
using TimeGuard.Core;

namespace TimeGuard.Agent;

/// <summary>
/// Клиент именованного канала к службе. Автоматически переподключается.
/// Читает push-статусы и ответы, отправляет коды и мастер-пароль.
///
/// Запись — прямо в поток без Flush(): PipeStream.Flush() блокируется до чтения
/// другой стороной, что при встречной записи даёт дедлок (см. PipeServer).
/// </summary>
public sealed class PipeClient : IDisposable
{
    private const string PipeName = "TimeGuard";
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly object _writeGate = new();
    private Stream? _writeStream;
    private CancellationTokenSource? _cts;

    public event Action<long, bool, bool, int?, int?>? StatusReceived; // remainingSeconds, isLocked, isNight
    public event Action<bool, string>? ResultReceived;  // accepted, message
    public event Action? Connected;
    public event Action? Disconnected;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => RunAsync(_cts.Token));
    }

    public void SendCode(string code) => Send(IpcMessage.Code(code));
    public void SendMaster(string password) => Send(IpcMessage.Master(password));
    public void ResetTimer() => Send(IpcMessage.Reset());
    public void RequestTime(string parentHost, int minutes) => Send(IpcMessage.TimeRequest(parentHost, minutes));

    private void Send(IpcMessage message)
    {
        lock (_writeGate)
        {
            if (_writeStream is null)
            {
                return;
            }
            try
            {
                byte[] bytes = Utf8.GetBytes(IpcSerializer.Serialize(message) + "\n");
                _writeStream.Write(bytes, 0, bytes.Length); // без Flush
            }
            catch (IOException)
            {
                // разрыв обработается в цикле чтения
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var stream = new NamedPipeClientStream(
                    ".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await stream.ConnectAsync(2000, ct).ConfigureAwait(false);

                using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);

                lock (_writeGate)
                {
                    _writeStream = stream;
                }
                Connected?.Invoke();
                Send(IpcMessage.Hello());

                while (!ct.IsCancellationRequested && stream.IsConnected)
                {
                    string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }
                    Program.Log("Канал <= " + line);
                    Dispatch(IpcSerializer.Deserialize(line));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // канал недоступен — повторим
            }
            finally
            {
                lock (_writeGate)
                {
                    _writeStream = null;
                }
                Disconnected?.Invoke();
            }

            try
            {
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void Dispatch(IpcMessage? msg)
    {
        if (msg is null)
        {
            return;
        }
        switch (msg.Kind)
        {
            case IpcKind.Status:
                StatusReceived?.Invoke(msg.RemainingSeconds ?? 0, msg.IsLocked ?? false, msg.IsNight ?? false, msg.CurfewStartHour, msg.CurfewEndHour);
                break;
            case IpcKind.Result:
                ResultReceived?.Invoke(msg.Accepted ?? false, msg.Text ?? string.Empty);
                break;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
