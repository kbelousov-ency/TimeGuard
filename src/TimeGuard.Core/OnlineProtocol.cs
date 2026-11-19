namespace TimeGuard.Core;

public sealed class OnlineControlCommand
{
    public string Action { get; set; } = string.Empty;
    public int Minutes { get; set; }
}

public sealed class OnlineControlResult
{
    public bool Accepted { get; set; }
    public string Message { get; set; } = string.Empty;
    public long RemainingSeconds { get; set; }
    public bool IsLocked { get; set; }
}

public sealed class OnlineTimeRequest
{
    public string MachineId { get; set; } = string.Empty;
    public int Minutes { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class OnlineTimeResponse
{
    public bool Accepted { get; set; }
    public string Message { get; set; } = string.Empty;
    public int AddedMinutes { get; set; }
    public long RemainingSeconds { get; set; }
}
