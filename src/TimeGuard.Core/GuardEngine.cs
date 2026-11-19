namespace TimeGuard.Core;

public readonly record struct GuardStatus(long RemainingSeconds, bool IsLocked, bool Night, int CurfewStartHour = 21, int CurfewEndHour = 9);

public readonly record struct SubmitResult(bool Accepted, string Message, int AddedMinutes);

/// <summary>
/// Авторитетная логика времени. Потокобезопасна: и тик времени, и обработка
/// кодов от агента идут под одним замком.
///
/// Блокировка выводится из двух правил:
///  - днём: экран заблокирован, когда бюджет времени исчерпан (RemainingSeconds ≤ 0);
///  - ночью (комендантский час, напр. 21:00–09:00): экран заблокирован всегда,
///    время не списывается, разблокировать можно ТОЛЬКО мастер-паролем — он
///    снимает запрет до конца этой ночи.
///
/// Коды привязаны ко времени (см. <see cref="TimeGuardCodec"/>). Защита от
/// повтора — оконная: принятый код запоминается по тегу до истечения его окна.
/// </summary>
public sealed class GuardEngine
{
    private const int MasterSessionMinutes = 120;

    private readonly IGuardStore _store;
    private readonly object _gate = new();
    private readonly GuardConfig _config;
    private readonly TimeGuardCodec _codec;
    private readonly GuardState _state;
    private readonly Func<long> _nowUnix;
    private readonly Func<DateTime> _nowLocal;

    public GuardEngine(
        IGuardStore store,
        GuardConfig config,
        GuardState state,
        Func<long>? nowUnix = null,
        Func<DateTime>? nowLocal = null)
    {
        _store = store;
        _config = config;
        _state = state;
        _state.ConsumedCodes ??= new Dictionary<string, long>();
        _state.UsedBackupCodes ??= new List<string>();
        _codec = new TimeGuardCodec(config.GetSecret(), config.MachineId);
        _nowUnix = nowUnix ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        _nowLocal = nowLocal ?? (() => DateTime.Now);
    }

    public GuardStatus Status
    {
        get
        {
            lock (_gate)
            {
                RefreshCurfew();
                return BuildStatus();
            }
        }
    }

    /// <summary>
    /// Списать реально прошедшие секунды (по монотонному счётчику), пока есть
    /// активная сессия и сейчас НЕ ночь. Возвращает статус и флаг перехода в блок.
    /// </summary>
    public (GuardStatus Status, bool JustLocked) Tick(long elapsedSeconds, bool userSessionActive)
    {
        lock (_gate)
        {
            bool wasLocked = IsLocked();
            RefreshCurfew();

            bool night = IsCurfewNow();
            if (userSessionActive && !night && _state.RemainingSeconds > 0 && elapsedSeconds > 0)
            {
                _state.RemainingSeconds -= elapsedSeconds;
                if (_state.RemainingSeconds < 0)
                {
                    _state.RemainingSeconds = 0;
                }
            }

            bool nowLocked = IsLocked();
            bool justLocked = nowLocked && !wasLocked;
            if (justLocked)
            {
                _store.SaveState(_state);
            }
            return (BuildStatus(), justLocked);
        }
    }

    public SubmitResult SubmitCode(string code)
    {
        lock (_gate)
        {
            RefreshCurfew();
            if (IsCurfewNow())
            {
                return new SubmitResult(false, "Ночью действует только мастер-пароль.", 0);
            }

            // Резервные коды — 10 цифр, без срока действия, одноразовые.
            string digits = new(code?.Where(char.IsDigit).ToArray() ?? Array.Empty<char>());
            if (digits.Length == 10)
            {
                return SubmitBackup(code!);
            }

            long now = _nowUnix();
            CodeVerifyResult r = _codec.Verify(code ?? string.Empty, now);
            switch (r.Status)
            {
                case CodeVerifyStatus.Ok:
                    PruneConsumed(now);
                    if (_state.ConsumedCodes.ContainsKey(r.Tag))
                    {
                        return new SubmitResult(false, "Этот код уже был использован.", 0);
                    }
                    _state.ConsumedCodes[r.Tag] = r.ExpireAtUnix;
                    _state.RemainingSeconds += r.Payload.Minutes * 60L;
                    _store.SaveState(_state);
                    return new SubmitResult(true, $"Добавлено {r.Payload.Minutes} мин.", r.Payload.Minutes);

                case CodeVerifyStatus.BadSignature:
                    return new SubmitResult(false, "Неверный или просроченный код.", 0);
                default:
                    return new SubmitResult(false, "Код не распознан.", 0);
            }
        }
    }

    private SubmitResult SubmitBackup(string code)
    {
        _state.UsedBackupCodes ??= new List<string>();
        if (!_codec.VerifyBackup(code, out int minutes, out string tag))
        {
            return new SubmitResult(false, "Неверный резервный код.", 0);
        }
        if (_state.UsedBackupCodes.Contains(tag))
        {
            return new SubmitResult(false, "Этот резервный код уже был использован.", 0);
        }
        _state.UsedBackupCodes.Add(tag);
        _state.RemainingSeconds += minutes * 60L;
        _store.SaveState(_state);
        return new SubmitResult(true, $"Резервный код: +{minutes} мин.", minutes);
    }

    public SubmitResult SubmitMaster(string password)
    {
        lock (_gate)
        {
            RefreshCurfew();
            bool ok = MasterPassword.Verify(password, _config.MasterHashBase64, _config.MasterSaltBase64);
            if (!ok)
            {
                return new SubmitResult(false, "Неверный мастер-пароль.", 0);
            }

            if (IsCurfewNow())
            {
                // снимаем ночной запрет до конца этой ночи (время ночью не идёт)
                _state.CurfewOverridden = true;
                _store.SaveState(_state);
                return new SubmitResult(true, "Ночной запрет снят до утра.", 0);
            }

            _state.RemainingSeconds += MasterSessionMinutes * 60L;
            _store.SaveState(_state);
            return new SubmitResult(true, $"Мастер-доступ: {MasterSessionMinutes} мин.", MasterSessionMinutes);
        }
    }

    public SubmitResult AddMinutes(int minutes)
    {
        lock (_gate)
        {
            if (minutes is < 1 or > TimeGuardCodec.MaxMinutes)
            {
                return new SubmitResult(false, $"Минуты должны быть в диапазоне 1..{TimeGuardCodec.MaxMinutes}.", 0);
            }
            _state.RemainingSeconds += minutes * 60L;
            _store.SaveState(_state);
            return new SubmitResult(true, $"Добавлено {minutes} мин.", minutes);
        }
    }

    public void Persist()
    {
        lock (_gate)
        {
            _store.SaveState(_state);
        }
    }

    /// <summary>Сбросить оставшееся время в 0 (мгновенная блокировка).</summary>
    public void ResetToZero()
    {
        lock (_gate)
        {
            _state.RemainingSeconds = 0;
            _state.CurfewOverridden = false;
            _store.SaveState(_state);
        }
    }

    // --- внутреннее ---

    private GuardStatus BuildStatus() =>
        new(Math.Max(0, _state.RemainingSeconds), IsLocked(), IsCurfewNow(), _config.CurfewStartHour, _config.CurfewEndHour);

    private bool IsLocked()
    {
        if (IsCurfewNow())
        {
            return !_state.CurfewOverridden;
        }
        return _state.RemainingSeconds <= 0;
    }

    private bool IsCurfewNow()
    {
        if (!_config.CurfewEnabled)
        {
            return false;
        }
        int hour = _nowLocal().Hour;
        int start = _config.CurfewStartHour;
        int end = _config.CurfewEndHour;
        if (start == end)
        {
            return false;
        }
        // Интервал через полночь (21..9): ночь, если час >= start ИЛИ час < end.
        return start < end ? (hour >= start && hour < end)
                           : (hour >= start || hour < end);
    }

    /// <summary>Снять флаг ночного разблока, когда ночь закончилась.</summary>
    private void RefreshCurfew()
    {
        if (_state.CurfewOverridden && !IsCurfewNow())
        {
            _state.CurfewOverridden = false;
        }
    }

    private void PruneConsumed(long now)
    {
        if (_state.ConsumedCodes.Count == 0)
        {
            return;
        }
        var expired = _state.ConsumedCodes.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList();
        foreach (string key in expired)
        {
            _state.ConsumedCodes.Remove(key);
        }
    }
}
