using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TimeGuard.Core;
using TimeGuard.Service.Interop;
using TimeGuard.Service.Ipc;
using TimeGuard.Service.Storage;

namespace TimeGuard.Service;

public sealed class Worker(
    ILogger<Worker> log,
    StateStore store,
    SessionLauncher launcher,
    PolicyGuard policy) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PersistInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PolicyInterval = TimeSpan.FromSeconds(10);

    private GuardEngine? _engine;
    private PipeServer? _pipe;
    private int? _agentPid;
    private bool? _lastSessionActive;
    private bool _timerWasRunning;
    private DateTimeOffset? _timerStartedAt;
    private string _discoveredParentHost = string.Empty;
    private string _targetUser = string.Empty;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        GuardConfig? config = store.LoadConfig();
        if (config is null)
        {
            log.LogWarning("Конфигурация не найдена ({Dir}). Служба ждёт установки пары.",
                @"%ProgramData%\TimeGuard");
            await WaitForConfigAsync(stoppingToken).ConfigureAwait(false);
            config = store.LoadConfig();
            if (config is null)
            {
                return;
            }
        }

        _targetUser = config.TargetUser ?? string.Empty;
        if (string.IsNullOrEmpty(_targetUser))
        {
            log.LogWarning("Целевая учётка не задана — ограничение применяется к любому вошедшему. " +
                "Задайте её: TimeGuard.Service.exe --set-user <имя>.");
        }
        else
        {
            log.LogInformation("Ограничение применяется только к учётке '{User}'.", _targetUser);
        }

        _engine = new GuardEngine(store, config, store.LoadState());
        _pipe = new PipeServer(
            log,
            code => _engine!.SubmitCode(code),
            master => _engine!.SubmitMaster(master),
            () => _engine!.Status,
            () => _engine!.ResetToZero(),
            (host, minutes) => RequestParentTimeAsync(config, host, minutes));
        _pipe.Start(stoppingToken);

        string agentPath = Path.Combine(AppContext.BaseDirectory, "TimeGuard.Agent.exe");
        string resetFlag = Path.Combine(store.DirectoryPath, "reset.flag");
        long lastTicks = _clock.ElapsedTicks;
        var lastPersist = DateTime.UtcNow;
        var lastPolicy = DateTime.MinValue;

        var activity = new ActivityLog(AppContext.BaseDirectory);
        // Сетевые компоненты необязательны для учёта времени: любой их сбой только логируется.
        await using var network = new NetworkComponents(log);
        network.Start(
            "сервер истории",
            () => new HistoryServer(
                log,
                config,
                limit => activity.ReadRecent(limit),
                command => HandleOnlineCommand(_engine!, command)),
            server => server.Start(stoppingToken));
        network.Start(
            "обнаружение родителя",
            () =>
            {
                var listener = new ParentDiscoveryListener(log, config.GetSecret(), config.MachineId);
                listener.ParentDiscovered += host => _discoveredParentHost = host;
                return listener;
            },
            listener => listener.Start(stoppingToken));
        activity.Write("Старт ПК (служба запущена).");
        var lastWall = DateTime.Now;
        var lastProcPoll = DateTime.MinValue;
        var trackedProcesses = new Dictionary<int, TrackedProcess>();

        log.LogInformation("Служба TimeGuard запущена. Агент: {Path}", agentPath);

        while (!stoppingToken.IsCancellationRequested)
        {
            long now = _clock.ElapsedTicks;
            long elapsedSeconds = (now - lastTicks) / Stopwatch.Frequency;
            if (elapsedSeconds > 0)
            {
                lastTicks += elapsedSeconds * Stopwatch.Frequency;
            }

            if (File.Exists(resetFlag))
            {
                _engine.ResetToZero();
                try { File.Delete(resetFlag); } catch (IOException) { }
                log.LogInformation("Время сброшено в 0 по запросу (reset.flag).");
            }

            // Детект сна: во сне процесс заморожен, монотонный тик стоит, а стенные
            // часы уходят вперёд. Большой разрыв = ПК спал.
            DateTime wall = DateTime.Now;
            TimeSpan wallGap = wall - lastWall;
            if (wallGap > TimeSpan.FromSeconds(90))
            {
                activity.Write($"Пробуждение после сна (сон ~{(int)wallGap.TotalMinutes} мин, уснул около {lastWall:HH:mm:ss}).");
            }
            lastWall = wall;

            bool sessionActive = IsTargetSessionActive();

            if (sessionActive != _lastSessionActive)
            {
                log.LogInformation("Целевая сессия активна: {Active}. Остаток: {Rem} c.",
                    sessionActive, _engine.Status.RemainingSeconds);
                _lastSessionActive = sessionActive;
            }
            bool chargeTime = sessionActive && launcher.IsActiveSessionUnlocked();
            (GuardStatus status, bool justLocked) = _engine.Tick(elapsedSeconds, chargeTime);
            _pipe.PushStatus(status);

            // Опрос приложений выполняется только пока реально списывается время.
            // Это исключает фоновые процессы при заблокированном экране Windows,
            // ночью после мастер-разблокировки и после исчерпания лимита.
            bool timerRunning = chargeTime && !status.IsLocked && !status.CurfewOverridden && status.RemainingSeconds > 0;
            if (timerRunning && !_timerWasRunning)
            {
                _timerStartedAt = DateTimeOffset.Now;
            }
            else if (!timerRunning)
            {
                _timerStartedAt = null;
            }
            _timerWasRunning = timerRunning;
            if (sessionActive && DateTime.UtcNow - lastProcPoll > TimeSpan.FromSeconds(3))
            {
                PollLaunchedApps(activity, trackedProcesses, timerRunning, _timerStartedAt);
                lastProcPoll = DateTime.UtcNow;
            }
            else if (!sessionActive && trackedProcesses.Count > 0)
            {
                CloseTrackedProcesses(activity, trackedProcesses, DateTimeOffset.Now);
            }
            if (justLocked)
            {
                log.LogInformation("Время исчерпано — экран блокируется.");
            }

            if (sessionActive)
            {
                EnsureAgentRunning(agentPath);
                if (DateTime.UtcNow - lastPolicy > PolicyInterval)
                {
                    string? sid = ActiveSessionUser.GetActiveSessionUserSid();
                    if (sid is not null)
                    {
                        policy.Apply(sid);
                    }
                    lastPolicy = DateTime.UtcNow;
                }
            }
            else
            {
                _agentPid = null;
            }

            if (DateTime.UtcNow - lastPersist > PersistInterval)
            {
                _engine.Persist();
                lastPersist = DateTime.UtcNow;
            }

            try
            {
                await Task.Delay(TickInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        CloseTrackedProcesses(activity, trackedProcesses, DateTimeOffset.Now);
        activity.Write("Выключение ПК / остановка службы.");
        _engine.Persist();
        if (_pipe is not null)
        {
            await _pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Владеет сетевыми компонентами: создание, запуск и остановка не бросают исключений.</summary>
    private sealed class NetworkComponents(ILogger log) : IAsyncDisposable
    {
        private readonly List<IAsyncDisposable> _items = [];

        public void Start<T>(string name, Func<T> create, Action<T> start) where T : class, IAsyncDisposable
        {
            T? item = null;
            try
            {
                item = create();
                start(item);
                _items.Add(item);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Не удалось запустить компонент «{Name}»; учёт времени продолжается без него.", name);
                if (item is not null)
                {
                    try { item.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                    catch (Exception) { }
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (IAsyncDisposable item in _items)
            {
                try { await item.DisposeAsync().ConfigureAwait(false); }
                catch (Exception) { }
            }
            _items.Clear();
        }
    }

    // Шумные системные процессы, которые не пишем в лог активности.
    private static readonly HashSet<string> IgnoredProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost", "csrss", "wininit", "winlogon", "services", "lsass", "smss", "fontdrvhost",
        "dwm", "sihost", "taskhostw", "ctfmon", "conhost", "dllhost", "RuntimeBroker",
        "backgroundTaskHost", "ApplicationFrameHost", "SearchHost", "StartMenuExperienceHost",
        "ShellExperienceHost", "TextInputHost", "SystemSettings", "WmiPrvSE", "smartscreen",
        "SecurityHealthSystray", "SecurityHealthService", "explorer", "spoolsv", "audiodg",
        "TimeGuard.Agent", "TimeGuard.Service", "SearchIndexer", "wallpaperservice32_c",
    };

    /// <summary>
    /// Логирует новые процессы в активной консольной сессии (приложения ребёнка).
    /// </summary>
    private readonly record struct TrackedProcess(string Name, DateTimeOffset StartedAt);

    private void PollLaunchedApps(
        ActivityLog activity,
        Dictionary<int, TrackedProcess> tracked,
        bool recordNewProcesses,
        DateTimeOffset? timerStartedAt)
    {
        uint session = launcher.GetActiveConsoleSessionId();
        if (session == uint.MaxValue)
        {
            return;
        }
        Process[] procs;
        try { procs = Process.GetProcesses(); }
        catch (Exception) { return; }

        var current = new HashSet<int>();
        foreach (Process p in procs)
        {
            try
            {
                if (p.SessionId != (int)session)
                {
                    continue;
                }
                string name = p.ProcessName;
                if (IgnoredProcesses.Contains(name))
                {
                    continue;
                }

                current.Add(p.Id);
                DateTimeOffset? processStart = TryGetProcessStartTime(p);
                if (tracked.TryGetValue(p.Id, out TrackedProcess previous))
                {
                    if (string.Equals(previous.Name, name, StringComparison.OrdinalIgnoreCase) &&
                        (processStart is null || previous.StartedAt == processStart.Value))
                    {
                        continue;
                    }

                    activity.CloseProcess(previous.Name, p.Id, previous.StartedAt, DateTimeOffset.Now);
                }
                if (!recordNewProcesses)
                {
                    tracked.Remove(p.Id);
                    continue;
                }
                if (timerStartedAt is not null && processStart is not null && processStart.Value < timerStartedAt.Value)
                {
                    continue;
                }
                DateTimeOffset startedAt = processStart ?? DateTimeOffset.Now;
                tracked[p.Id] = new TrackedProcess(name, startedAt);
                activity.StartProcess(name, p.Id, startedAt);
            }
            catch (Exception)
            {
                // процесс мог завершиться / нет доступа — пропускаем
            }
            finally
            {
                p.Dispose();
            }
        }

        foreach (int pid in tracked.Keys.Where(pid => !current.Contains(pid)).ToArray())
        {
            TrackedProcess process = tracked[pid];
            activity.CloseProcess(process.Name, pid, process.StartedAt, DateTimeOffset.Now);
            tracked.Remove(pid);
        }
    }

    private static DateTimeOffset? TryGetProcessStartTime(Process process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void CloseTrackedProcesses(
        ActivityLog activity,
        Dictionary<int, TrackedProcess> tracked,
        DateTimeOffset closedAt)
    {
        foreach ((int pid, TrackedProcess process) in tracked)
        {
            activity.CloseProcess(process.Name, pid, process.StartedAt, closedAt);
        }
        tracked.Clear();
    }

    private static OnlineControlResult HandleOnlineCommand(GuardEngine engine, OnlineControlCommand command)
    {
        if (command.Action == "status")
        {
            GuardStatus current = engine.Status;
            return new OnlineControlResult { Accepted = true, Message = "Статус получен.", RemainingSeconds = current.RemainingSeconds, IsLocked = current.IsLocked };
        }
        SubmitResult result = command.Action switch
        {
            "add" => engine.AddMinutes(command.Minutes),
            "reset" => Reset(engine),
            _ => new SubmitResult(false, "Неизвестная онлайн-команда.", 0),
        };
        GuardStatus status = engine.Status;
        return new OnlineControlResult
        {
            Accepted = result.Accepted,
            Message = result.Message,
            RemainingSeconds = status.RemainingSeconds,
            IsLocked = status.IsLocked,
        };
    }

    private static SubmitResult Reset(GuardEngine engine)
    {
        engine.ResetToZero();
        return new SubmitResult(true, "Таймер сброшен.", 0);
    }

    private async Task<SubmitResult> RequestParentTimeAsync(GuardConfig config, string host, int minutes)
    {
        if (minutes is < 1 or > TimeGuardCodec.MaxMinutes)
        {
            return new SubmitResult(false, "Некорректное количество минут.", 0);
        }
        string value = string.IsNullOrWhiteSpace(host) ? _discoveredParentHost : host.Trim();
        if (!value.Contains("://", StringComparison.Ordinal)) value = "http://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed))
        {
            return new SubmitResult(false, "Адрес родителя указан неверно.", 0);
        }
        var builder = new UriBuilder(parsed)
        {
            Port = parsed.IsDefaultPort ? HistoryProtocol.Port + 1 : parsed.Port,
            Path = HistoryProtocol.ParentRequestRoute,
        };
        byte[] requestBody = JsonSerializer.SerializeToUtf8Bytes(new OnlineTimeRequest
        {
            MachineId = config.MachineId,
            Minutes = minutes,
            Reason = "Запрос из приложения клиента",
        });
        using HttpRequestMessage request = HistoryProtocol.CreateSignedRequest(
            HttpMethod.Post, builder.Uri, config.GetSecret(), config.MachineId, HistoryProtocol.ParentRequestRoute, requestBody);
        // родитель ждёт решения до 5 минут и затем ещё отправляет команду add на этот ПК
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(6) };
        using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return new SubmitResult(false, $"Родитель вернул HTTP {(int)response.StatusCode}.", 0);
        // Ответ не подписан, а адрес родителя может подменить ребёнок: минуты здесь не добавляются.
        // При одобрении родитель сам присылает подписанную команду add на /timeguard/control.
        OnlineTimeResponse result = JsonSerializer.Deserialize<OnlineTimeResponse>(body) ?? new OnlineTimeResponse { Message = "Пустой ответ родителя." };
        return new SubmitResult(result.Accepted, result.Message, result.AddedMinutes);
    }

    private DateTime _lastAgentAttempt = DateTime.MinValue;

    private void EnsureAgentRunning(string agentPath)
    {
        if (_agentPid is int pid && IsAlive(pid))
        {
            return;
        }
        if (!File.Exists(agentPath))
        {
            log.LogWarning("Файл агента не найден: {Path}", agentPath);
            return;
        }
        // не чаще раза в 5 секунд, чтобы не засорять журнал при неудачах
        if (DateTime.UtcNow - _lastAgentAttempt < TimeSpan.FromSeconds(5))
        {
            return;
        }
        _lastAgentAttempt = DateTime.UtcNow;
        try
        {
            _agentPid = launcher.LaunchInActiveSession(agentPath);
            if (_agentPid is null)
            {
                log.LogWarning("Запуск агента не выполнен (нет доступной сессии/токена).");
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Ошибка запуска агента.");
        }
    }

    /// <summary>
    /// Активна ли сессия ЦЕЛЕВОГО ребёнка. Если целевая учётка не задана —
    /// как раньше, любой вошедший. Иначе только совпадение имени учётки.
    /// </summary>
    private bool IsTargetSessionActive()
    {
        if (!launcher.HasActiveUserSession())
        {
            return false;
        }
        if (string.IsNullOrEmpty(_targetUser))
        {
            return true;
        }
        string? active = ActiveSessionUser.GetActiveSessionUserName();
        return active is not null && string.Equals(active, _targetUser, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private async Task WaitForConfigAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !store.ConfigExists)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
