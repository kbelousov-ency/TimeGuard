using System.Windows;
using WinFormsScreen = System.Windows.Forms.Screen;

namespace TimeGuard.Agent;

/// <summary>
/// Оркестратор агента: держит индикатор, показывает/скрывает заглушку по статусу
/// от службы и включает клавиатурный хук на время блокировки.
/// </summary>
public sealed class AgentApp : Application
{
    private readonly PipeClient _pipe = new();
    private readonly KeyboardHook _hook = new();
    private IndicatorWindow? _indicator;
    private readonly List<OverlayWindow> _overlays = new();
    private OverlayWindow? _primaryOverlay;
    private bool _locked;
    private bool _canExtend;
    private long _remaining;
    private ExtendTimeWindow? _extension;
    private ClientControlWindow? _controlWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += (_, args) =>
        {
            Program.Log("Необработанное исключение UI: " + args.Exception);
            args.Handled = true; // не роняем процесс, чтобы увидеть лог
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Program.Log("Необработанное исключение домена: " + args.ExceptionObject);

        try
        {
            Program.Log("OnStartup: создаю индикатор...");
            _indicator = new IndicatorWindow();
            _indicator.ExtendRequested += OpenExtension;
            _indicator.Show();
            Program.Log("OnStartup: индикатор показан.");

            _pipe.Connected += () => Program.Log("Канал: подключён к службе.");
            _pipe.Disconnected += () => Program.Log("Канал: отключён.");
            _pipe.StatusReceived += (secs, locked, night, start, end) =>
                Dispatcher.Invoke(() => OnStatus(secs, locked, night, start, end));
            _pipe.ResultReceived += (ok, msg) =>
                Dispatcher.Invoke(() =>
                {
                    _primaryOverlay?.SetMessage(msg);
                    _extension?.SetResult(ok, msg);
                    _controlWindow?.SetResult(ok, msg);
                });
            _pipe.Disconnected += () =>
                Dispatcher.Invoke(() =>
                {
                    _canExtend = false;
                    _indicator?.SetCanExtend(false);
                    _indicator?.SetRemaining(0);
                    _extension?.Close();
                    _controlWindow?.Close();
                });
            _pipe.Start();
            Program.Log("OnStartup: канал запущен, ждём статус от службы.");
        }
        catch (Exception ex)
        {
            Program.Log("OnStartup ОШИБКА: " + ex);
            throw;
        }
    }

    private void OnStatus(long remainingSeconds, bool locked, bool night, int? start, int? end)
    {
        Program.Log($"Статус от службы: остаток={remainingSeconds} c, locked={locked}, night={night}, было_locked={_locked}");
        _indicator?.SetRemaining(remainingSeconds);
        _remaining = remainingSeconds;
        _controlWindow?.SetRemaining(remainingSeconds);
        _canExtend = !locked && !night && remainingSeconds > 0;
        _indicator?.SetCanExtend(_canExtend);
        if (!_canExtend) _extension?.Close();
        else _extension?.SetRemaining(remainingSeconds);

        if (locked && !_locked)
        {
            ShowOverlays(night, start, end);
        }
        else if (!locked && _locked)
        {
            HideOverlays();
        }
        _locked = locked;

        foreach (OverlayWindow o in _overlays)
        {
            o.SetNight(night, start, end);
        }
    }

    private void OpenExtension()
    {
        if (_controlWindow is not null) { _controlWindow.Activate(); return; }
        var window = new ClientControlWindow();
        _controlWindow = window;
        window.SetRemaining(_remaining);
        window.CodeSubmitted += code => _pipe.SendCode(code);
        window.ResetRequested += () => _pipe.ResetTimer();
        window.TimeRequested += (host, minutes) => _pipe.RequestTime(host, minutes);
        window.Closed += (_, _) => _controlWindow = null;
        window.Show(); window.Activate();
    }

    private void ShowOverlays(bool night, int? start, int? end)
    {
        try
        {
            WinFormsScreen[] screens = WinFormsScreen.AllScreens;
            Program.Log($"Показываю заглушку. Экранов: {screens.Length}");
            foreach (WinFormsScreen screen in screens)
            {
                bool host = screen.Primary;
                var overlay = new OverlayWindow(screen, host, night, start, end);
                if (host)
                {
                    _primaryOverlay = overlay;
                    overlay.CodeSubmitted += code => _pipe.SendCode(code);
                    overlay.MasterSubmitted += pwd => _pipe.SendMaster(pwd);
                }
                overlay.Show();
                _overlays.Add(overlay);
            }
            _hook.Enable();
            Program.Log("Заглушка показана.");
        }
        catch (Exception ex)
        {
            Program.Log("ОШИБКА показа заглушки: " + ex);
        }
    }

    private void HideOverlays()
    {
        _hook.Disable();
        foreach (OverlayWindow overlay in _overlays)
        {
            overlay.CloseOverlay();
        }
        _overlays.Clear();
        _primaryOverlay = null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook.Dispose();
        _pipe.Dispose();
        base.OnExit(e);
    }
}
