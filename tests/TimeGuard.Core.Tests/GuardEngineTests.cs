using System.Security.Cryptography;
using TimeGuard.Core;
using Xunit;

namespace TimeGuard.Core.Tests;

/// <summary>In-memory хранилище для тестов движка без DPAPI/Windows.</summary>
internal sealed class FakeStore : IGuardStore
{
    private GuardState _state;
    public FakeStore(GuardState initial) => _state = initial;
    public GuardState LoadState() => _state;
    public void SaveState(GuardState state) => _state = state;
}

/// <summary>Управляемые часы для детерминированных тестов времени.</summary>
internal sealed class FakeClock
{
    public long Now;
    public FakeClock(long start) => Now = start;
    public long Get() => Now;
}

public class GuardEngineTests
{
    private const string Machine = "client-xyz";
    private const long T0 = 1_700_000_000;

    // localHour фиксирует локальное время (для проверок дня/ночи). 12 = день.
    private static (GuardEngine engine, TimeGuardCodec parent, FakeClock clock) Build(
        long remaining, int localHour = 12)
    {
        byte[] secret = RandomNumberGenerator.GetBytes(32);
        var (hash, salt) = MasterPassword.Create("master-pass");
        var cfg = new GuardConfig
        {
            SecretBase64 = Convert.ToBase64String(secret),
            MachineId = Machine,
            MasterHashBase64 = hash,
            MasterSaltBase64 = salt,
        };
        var store = new FakeStore(new GuardState { RemainingSeconds = remaining });
        var clock = new FakeClock(T0);
        var engine = new GuardEngine(
            store, cfg, store.LoadState(), clock.Get, () => new DateTime(2026, 1, 1, localHour, 0, 0));
        var parent = new TimeGuardCodec(secret, Machine);
        return (engine, parent, clock);
    }

    [Fact]
    public void Time_is_consumed_only_when_session_active_and_unlocked()
    {
        var (engine, _, _) = Build(remaining: 100);

        var (s1, _) = engine.Tick(10, userSessionActive: true);
        Assert.Equal(90, s1.RemainingSeconds);
        Assert.False(s1.IsLocked);

        var (s2, _) = engine.Tick(10, userSessionActive: false);
        Assert.Equal(90, s2.RemainingSeconds);
    }

    [Fact]
    public void Reaching_zero_locks_and_reports_just_locked_once()
    {
        var (engine, _, _) = Build(remaining: 5);

        var (s1, justLocked1) = engine.Tick(10, true);
        Assert.True(s1.IsLocked);
        Assert.Equal(0, s1.RemainingSeconds);
        Assert.True(justLocked1);

        var (_, justLocked2) = engine.Tick(1, true);
        Assert.False(justLocked2);
    }

    [Fact]
    public void Grant_code_adds_time_and_unlocks()
    {
        var (engine, parent, clock) = Build(remaining: 0);
        engine.Tick(1, true);
        Assert.True(engine.Status.IsLocked);

        string code = parent.Generate(10, clock.Now);
        SubmitResult r = engine.SubmitCode(code);

        Assert.True(r.Accepted);
        Assert.Equal(10, r.AddedMinutes);
        Assert.False(engine.Status.IsLocked);
        Assert.Equal(600, engine.Status.RemainingSeconds);
    }

    [Fact]
    public void Same_code_twice_is_rejected_within_window()
    {
        var (engine, parent, clock) = Build(remaining: 0);
        string code = parent.Generate(10, clock.Now);

        Assert.True(engine.SubmitCode(code).Accepted);
        Assert.False(engine.SubmitCode(code).Accepted); // повтор в окне
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Extension_adds_to_running_balance_without_replacing_it(bool backup)
    {
        var (engine, parent, clock) = Build(remaining: 900);
        engine.Tick(37, true); // Время, потраченное до отправки кода.
        string code = backup ? parent.GenerateBackup(0, 20) : parent.Generate(20, clock.Now);
        Assert.True(engine.SubmitCode(code).Accepted);
        Assert.Equal(2063, engine.Status.RemainingSeconds);
        Assert.False(engine.Status.IsLocked);
        Assert.False(engine.SubmitCode(code).Accepted);
        engine.Tick(3, true);
        Assert.Equal(2060, engine.Status.RemainingSeconds);
    }

    [Fact]
    public void Expired_code_is_rejected_as_invalid_not_replay()
    {
        var (engine, parent, clock) = Build(remaining: 0);
        string code = parent.Generate(10, clock.Now);

        // уводим часы далеко за окно
        clock.Now += (TimeGuardCodec.WindowSteps + 5) * TimeGuardCodec.PeriodSeconds;
        SubmitResult r = engine.SubmitCode(code);
        Assert.False(r.Accepted);
        Assert.Contains("просроченный", r.Message);
    }

    [Fact]
    public void Different_parents_codes_both_accepted()
    {
        var (engine, parentA, clock) = Build(remaining: 0);
        // второй родитель с тем же секретом эмулируется тем же codec-ключом:
        string a = parentA.Generate(15, clock.Now);
        string b = parentA.Generate(30, clock.Now);

        Assert.True(engine.SubmitCode(a).Accepted);
        Assert.True(engine.SubmitCode(b).Accepted);
        Assert.Equal((15 + 30) * 60, engine.Status.RemainingSeconds);
    }

    [Fact]
    public void Master_password_unlocks_with_session_grant()
    {
        var (engine, _, _) = Build(remaining: 0);
        engine.Tick(1, true);
        Assert.True(engine.Status.IsLocked);

        Assert.False(engine.SubmitMaster("wrong").Accepted);
        Assert.True(engine.SubmitMaster("master-pass").Accepted);
        Assert.False(engine.Status.IsLocked);
    }

    [Fact]
    public void Wrong_secret_code_is_rejected_by_engine()
    {
        var (engine, _, clock) = Build(remaining: 0);
        var foreign = new TimeGuardCodec(RandomNumberGenerator.GetBytes(32), Machine);
        Assert.False(engine.SubmitCode(foreign.Generate(10, clock.Now)).Accepted);
    }

    // --- резервные коды ---

    [Fact]
    public void Backup_code_adds_time_and_unlocks_day()
    {
        var (engine, parent, _) = Build(remaining: 0, localHour: 12);
        Assert.True(engine.Status.IsLocked);

        SubmitResult r = engine.SubmitCode(parent.GenerateBackup(0, 20));
        Assert.True(r.Accepted);
        Assert.Equal(20 * 60, engine.Status.RemainingSeconds);
        Assert.False(engine.Status.IsLocked);
    }

    [Fact]
    public void Backup_code_works_only_once()
    {
        var (engine, parent, _) = Build(remaining: 0, localHour: 12);
        string code = parent.GenerateBackup(0, 20);
        Assert.True(engine.SubmitCode(code).Accepted);
        Assert.False(engine.SubmitCode(code).Accepted); // повторно нельзя
    }

    [Fact]
    public void Different_backup_indexes_are_independent()
    {
        var (engine, parent, _) = Build(remaining: 0, localHour: 12);
        Assert.True(engine.SubmitCode(parent.GenerateBackup(0, 10)).Accepted);
        Assert.True(engine.SubmitCode(parent.GenerateBackup(1, 10)).Accepted);
        Assert.Equal(20 * 60, engine.Status.RemainingSeconds);
    }

    [Fact]
    public void Backup_code_rejected_at_night()
    {
        var (engine, parent, _) = Build(remaining: 0, localHour: 23);
        SubmitResult r = engine.SubmitCode(parent.GenerateBackup(0, 20));
        Assert.False(r.Accepted);
        Assert.Contains("мастер-пароль", r.Message);
    }

    // --- ночной запрет (комендантский час) ---

    [Fact]
    public void At_night_screen_is_locked_even_with_time_left()
    {
        var (engine, _, _) = Build(remaining: 3600, localHour: 23);
        Assert.True(engine.Status.IsLocked);
        Assert.True(engine.Status.Night);
    }

    [Fact]
    public void At_night_time_is_not_consumed()
    {
        var (engine, _, _) = Build(remaining: 3600, localHour: 2);
        var (s, _) = engine.Tick(60, userSessionActive: true);
        Assert.Equal(3600, s.RemainingSeconds); // ночью бюджет не тратится
    }

    [Fact]
    public void At_night_codes_are_rejected()
    {
        var (engine, parent, clock) = Build(remaining: 0, localHour: 23);
        SubmitResult r = engine.SubmitCode(parent.Generate(30, clock.Now));
        Assert.False(r.Accepted);
        Assert.Contains("мастер-пароль", r.Message);
    }

    [Fact]
    public void At_night_master_unlocks_until_morning()
    {
        var (engine, _, _) = Build(remaining: 0, localHour: 23);
        Assert.True(engine.Status.IsLocked);

        Assert.True(engine.SubmitMaster("master-pass").Accepted);
        Assert.False(engine.Status.IsLocked); // ночной запрет снят
    }

    [Fact]
    public void Day_uses_budget_not_curfew()
    {
        var (engine, _, _) = Build(remaining: 100, localHour: 12);
        Assert.False(engine.Status.IsLocked);
        Assert.False(engine.Status.Night);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(23)]
    public void Reset_revokes_master_access_and_preserves_used_codes(int hour)
    {
        var (engine, parent, clock) = Build(remaining: 100, localHour: hour);
        string code = parent.Generate(10, clock.Now);
        if (hour == 12)
        {
            Assert.True(engine.SubmitCode(code).Accepted);
        }
        Assert.True(engine.SubmitMaster("master-pass").Accepted);
        Assert.False(engine.Status.IsLocked);

        engine.ResetToZero();

        Assert.Equal(0, engine.Status.RemainingSeconds);
        Assert.True(engine.Status.IsLocked);
        Assert.False(engine.SubmitCode(code).Accepted);
        Assert.True(engine.SubmitMaster("master-pass").Accepted);
        Assert.False(engine.Status.IsLocked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Night_codes_remain_rejected_after_master_unlock(bool backup)
    {
        var (engine, parent, clock) = Build(remaining: 100, localHour: 23);
        Assert.True(engine.SubmitMaster("master-pass").Accepted);
        string code = backup ? parent.GenerateBackup(0, 30) : parent.Generate(30, clock.Now);

        Assert.False(engine.SubmitCode(code).Accepted);
        var (status, _) = engine.Tick(60, true);
        Assert.Equal(100, status.RemainingSeconds);
        Assert.False(status.IsLocked);
    }

    [Fact]
    public void Curfew_can_be_disabled()
    {
        byte[] secret = RandomNumberGenerator.GetBytes(32);
        var (hash, salt) = MasterPassword.Create("m");
        var cfg = new GuardConfig
        {
            SecretBase64 = Convert.ToBase64String(secret),
            MachineId = Machine,
            MasterHashBase64 = hash,
            MasterSaltBase64 = salt,
            CurfewEnabled = false,
        };
        var store = new FakeStore(new GuardState { RemainingSeconds = 100 });
        var engine = new GuardEngine(
            store, cfg, store.LoadState(), () => T0, () => new DateTime(2026, 1, 1, 23, 0, 0));
        Assert.False(engine.Status.IsLocked); // запрет выключен — ночью не блокирует
    }
}
