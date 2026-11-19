using TimeGuard.Core;
using Xunit;

namespace TimeGuard.Core.Tests;

public sealed class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.4", 1, 2, 4)]
    [InlineData("V1.2.4", 1, 2, 4)]
    [InlineData("1.2.4", 1, 2, 4)]
    [InlineData(" v1.10.0 ", 1, 10, 0)]
    [InlineData("v2.0", 2, 0, 0)]
    public void ParseTag_reads_version(string tag, int major, int minor, int build)
    {
        Assert.Equal(new Version(major, minor, build, 0), UpdateChecker.ParseTag(tag));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("releases")]
    [InlineData("v")]
    [InlineData("v1.x")]
    [InlineData("v1")]
    public void ParseTag_rejects_non_versions(string? tag)
    {
        Assert.Null(UpdateChecker.ParseTag(tag));
    }

    [Theory]
    [InlineData("1.2.5", "1.2.4", true)]
    [InlineData("1.3.0", "1.2.9", true)]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("1.2.4", "1.2.4", false)]
    [InlineData("1.2.3", "1.2.4", false)]
    [InlineData("1.2.4", "1.2.4.0", false)]
    [InlineData("1.2.4.0", "1.2.4", false)]
    public void IsNewer_compares_numerically(string latest, string current, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.IsNewer(Version.Parse(latest), Version.Parse(current)));
    }

    [Fact]
    public void Current_version_is_embedded_in_assembly()
    {
        Assert.True(UpdateChecker.CurrentVersion > new Version(0, 0, 0, 0));
    }

    [Fact]
    public void Result_reports_whether_update_is_needed()
    {
        Assert.False(new UpdateCheckResult(new Version(1, 2, 4, 0), new Version(1, 2, 4, 0)).IsNewer);
        Assert.True(new UpdateCheckResult(new Version(1, 2, 4, 0), new Version(1, 2, 5, 0)).IsNewer);
    }
}
