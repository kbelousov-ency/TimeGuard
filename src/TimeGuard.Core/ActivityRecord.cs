namespace TimeGuard.Core;

/// <summary>Одна сессия работы приложения, обнаруженная службой.</summary>
public sealed class ActivityRecord
{
    public string ProcessName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}
