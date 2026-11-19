using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.Logging;
using TimeGuard.Core;

namespace TimeGuard.Service.Ipc;

/// <summary>
/// Сервер именованного канала \.\pipe\TimeGuard. Обслуживает единственного
/// агента: принимает коды/мастер-пароль и отправляет push-обновления статуса.
///
/// ВАЖНО: запись идёт прямо в поток без Flush(). На именованном канале
/// PipeStream.Flush() вызывает FlushFileBuffers, который блокируется до тех пор,
/// пока другая сторона не вычитает данные. Если обе стороны флашат до чтения —
/// возникает взаимный дедлок. Прямая запись байт доставляется читателю без Flush.
/// </summary>
public sealed class PipeServer : IAsyncDisposable
{
    public const string PipeName = "TimeGuard";
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly ILogger _log;
    private readonly Func<string, SubmitResult> _onCode;
    private readonly Func<string, SubmitResult> _onMaster;
    private readonly Action _onReset;
    private readonly Func<string, int, Task<SubmitResult>> _onTimeRequest;
    private readonly Func<GuardStatus> _statusProvider;

    private readonly object _writeGate = new();
    private Stream? _writeStream;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public PipeServer(
        ILogger log,
        Func<string, SubmitResult> onCode,
        Func<string, SubmitResult> onMaster,
        Func<GuardStatus> statusProvider,
        Action? onReset = null,
        Func<string, int, Task<SubmitResult>>? onTimeRequest = null)
    {
        _log = log;
        _onCode = onCode;
        _onMaster = onMaster;
        _onReset = onReset ?? (() => { });
        _onTimeRequest = onTimeRequest ?? ((_, _) => Task.FromResult(new SubmitResult(false, "Онлайн-запрос недоступен.", 0)));
        _statusProvider = statusProvider;
    }

    public void Start(CancellationToken stopToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(stopToken);
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public void PushStatus(GuardStatus status) =>
        WriteMessage(IpcMessage.Status(status.RemainingSeconds, status.IsLocked, status.Night, status.CurfewStartHour, status.CurfewEndHour));

    private void Reply(SubmitResult result) =>
        WriteMessage(IpcMessage.Result(result.Accepted, result.Message, result.AddedMinutes));

    private void WriteMessage(IpcMessage message)
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
                _writeStream.Write(bytes, 0, bytes.Length); // без Flush — иначе дедлок
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Ошибка записи в канал — считаю агент отключённым.");
                _writeStream = null;
            }
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using NamedPipeServerStream server = CreateServer();
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                await HandleClientAsync(server, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Ошибка канала, перезапуск приёма.");
                try { await Task.Delay(500, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true);

        lock (_writeGate)
        {
            _writeStream = server;
        }

        GuardStatus initial = _statusProvider();
        _log.LogInformation("Агент подключился к каналу. Отправляю статус: остаток={Rem} c, locked={Lock}.",
            initial.RemainingSeconds, initial.IsLocked);
        PushStatus(initial);

        try
        {
            while (!ct.IsCancellationRequested && server.IsConnected)
            {
                string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }
                IpcMessage? msg = IpcSerializer.Deserialize(line);
                if (msg is not null)
                {
                    HandleMessage(msg);
                }
            }
        }
        finally
        {
            lock (_writeGate)
            {
                if (ReferenceEquals(_writeStream, server))
                {
                    _writeStream = null;
                }
            }
        }
    }

    private void HandleMessage(IpcMessage msg)
    {
        switch (msg.Kind)
        {
            case IpcKind.Hello:
                PushStatus(_statusProvider());
                break;
            case IpcKind.SubmitCode:
                Reply(_onCode(msg.Text ?? string.Empty));
                break;
            case IpcKind.SubmitMaster:
                Reply(_onMaster(msg.Text ?? string.Empty));
                break;
            case IpcKind.ResetTimer:
                _onReset();
                PushStatus(_statusProvider());
                break;
            case IpcKind.RequestTime:
                _ = ReplyTimeRequestAsync(msg.Text ?? string.Empty, msg.Minutes ?? 0);
                break;
        }
    }

    private async Task ReplyTimeRequestAsync(string host, int minutes)
    {
        SubmitResult result;
        try { result = await _onTimeRequest(host, minutes).ConfigureAwait(false); }
        catch (Exception ex) { result = new SubmitResult(false, ex.Message, 0); }
        Reply(result);
    }

    private static NamedPipeServerStream CreateServer()
    {
        var security = new PipeSecurity();
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var interactive = new SecurityIdentifier(WellKnownSidType.InteractiveSid, null);

        security.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(admins, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(interactive,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 4096,
            outBufferSize: 4096,
            security);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _cts?.Cancel();
            if (_loop is not null)
            {
                await _loop.ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // игнорируем при остановке
        }
        _cts?.Dispose();
    }
}
