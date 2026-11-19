using TimeGuard.Core;
using Xunit;

namespace TimeGuard.Core.Tests;

public sealed class ReplayGuardTests
{
    private const long Now = 1_700_000_000;
    private const long Window = HistoryProtocol.TimestampWindowSeconds;

    [Fact]
    public void Same_signature_is_accepted_only_once()
    {
        var guard = new ReplayGuard();

        Assert.True(guard.TryAccept(Now, "abc123", Now));
        Assert.False(guard.TryAccept(Now, "abc123", Now));
        Assert.False(guard.TryAccept(Now, "ABC123", Now + 10));
        Assert.True(guard.TryAccept(Now, "def456", Now));
        Assert.True(guard.TryAccept(Now + 1, "abc123", Now));
    }

    [Fact]
    public void Timestamp_outside_window_is_rejected()
    {
        var guard = new ReplayGuard();

        Assert.False(guard.TryAccept(Now - Window - 1, "old", Now));
        Assert.False(guard.TryAccept(Now + Window + 1, "future", Now));
        Assert.Equal(0, guard.Count);
    }

    [Fact]
    public void Entries_are_forgotten_after_timestamp_leaves_window()
    {
        var guard = new ReplayGuard();
        Assert.True(guard.TryAccept(Now, "abc123", Now));

        Assert.False(guard.TryAccept(Now, "abc123", Now + Window));
        Assert.True(guard.TryAccept(Now + Window + 1, "other", Now + Window + 1));
        Assert.Equal(1, guard.Count);
        // сама подпись из прошлого окна уже не пройдёт: её timestamp вне окна
        Assert.False(guard.TryAccept(Now, "abc123", Now + Window + 1));
    }
}
