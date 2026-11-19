namespace TimeGuard.Core;

/// <summary>
/// Помнит принятые подписанные LAN-запросы (timestamp, signature), пока timestamp не вышел из окна,
/// и отклоняет их повтор. Вызывать только после успешной проверки подписи.
/// </summary>
public sealed class ReplayGuard
{
    private readonly long _windowSeconds;
    private readonly HashSet<(long Timestamp, string Signature)> _accepted = new();
    private readonly object _gate = new();

    public ReplayGuard(long windowSeconds = HistoryProtocol.TimestampWindowSeconds) => _windowSeconds = windowSeconds;

    public int Count
    {
        get { lock (_gate) return _accepted.Count; }
    }

    /// <summary>true — запрос виден впервые и запомнен; false — повтор или timestamp вне окна.</summary>
    public bool TryAccept(long timestamp, string signature, long nowUnixSeconds)
    {
        // hex регистронезависим при проверке подписи, поэтому и ключ повтора не должен зависеть от регистра
        string key = signature.Trim().ToLowerInvariant();
        lock (_gate)
        {
            long oldest = nowUnixSeconds - _windowSeconds;
            _accepted.RemoveWhere(entry => entry.Timestamp < oldest);
            if (timestamp < oldest || timestamp > nowUnixSeconds + _windowSeconds)
            {
                return false;
            }
            return _accepted.Add((timestamp, key));
        }
    }
}
