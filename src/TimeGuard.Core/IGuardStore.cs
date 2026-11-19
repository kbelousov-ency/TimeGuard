namespace TimeGuard.Core;

/// <summary>Абстракция персиста состояния для движка (реальный — DPAPI в службе).</summary>
public interface IGuardStore
{
    GuardState LoadState();
    void SaveState(GuardState state);
}
