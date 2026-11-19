using TimeGuard.Core;
using Xunit;

namespace TimeGuard.Core.Tests;

public sealed class DiscoveryProtocolTests
{
    private const long Now = 1_700_000_000;
    private static readonly byte[] Secret = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] OtherSecret = Enumerable.Range(100, 32).Select(i => (byte)i).ToArray();

    private static DiscoveryBeacon SignedBeacon(long timestamp = Now, byte[]? secret = null)
    {
        var beacon = new DiscoveryBeacon
        {
            Kind = DiscoveryProtocol.ParentKind,
            MachineId = "11111111-2222-3333-4444-555555555555",
            Name = "PARENT-PC",
            Port = HistoryProtocol.Port + 1,
            Timestamp = timestamp,
        };
        beacon.Signature = DiscoveryProtocol.Sign(secret ?? Secret, beacon);
        return beacon;
    }

    [Fact]
    public void Valid_beacon_passes()
    {
        Assert.True(DiscoveryProtocol.IsValid(Secret, SignedBeacon(), Now));
    }

    [Fact]
    public void Valid_beacon_survives_serialization_roundtrip()
    {
        DiscoveryBeacon? parsed = DiscoveryProtocol.Deserialize(DiscoveryProtocol.Serialize(SignedBeacon()));

        Assert.NotNull(parsed);
        Assert.True(DiscoveryProtocol.IsValid(Secret, parsed, Now));
    }

    [Fact]
    public void Changed_port_breaks_signature()
    {
        DiscoveryBeacon beacon = SignedBeacon();
        beacon.Port = 1234;

        Assert.False(DiscoveryProtocol.IsValid(Secret, beacon, Now));
    }

    [Fact]
    public void Changed_name_breaks_signature()
    {
        DiscoveryBeacon beacon = SignedBeacon();
        beacon.Name = "EVIL-PC";

        Assert.False(DiscoveryProtocol.IsValid(Secret, beacon, Now));
    }

    [Fact]
    public void Foreign_secret_is_rejected()
    {
        DiscoveryBeacon beacon = SignedBeacon(secret: OtherSecret);

        Assert.False(DiscoveryProtocol.IsValid(Secret, beacon, Now));
    }

    [Theory]
    [InlineData(-30, true)]
    [InlineData(30, true)]
    [InlineData(-31, false)]
    [InlineData(31, false)]
    [InlineData(-3600, false)]
    [InlineData(3600, false)]
    public void Timestamp_is_accepted_only_within_30_seconds(long offset, bool expected)
    {
        DiscoveryBeacon beacon = SignedBeacon(timestamp: Now + offset);

        Assert.Equal(expected, DiscoveryProtocol.IsValid(Secret, beacon, Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"kind\":")]
    [InlineData("{\"kind\":\"TimeGuard.Parent\",\"port\":\"not a number\"}")]
    public void Malformed_json_yields_null(string text)
    {
        Assert.Null(DiscoveryProtocol.Deserialize(text));
    }

    [Fact]
    public void Unsigned_beacon_is_rejected()
    {
        DiscoveryBeacon beacon = SignedBeacon();
        beacon.Signature = string.Empty;

        Assert.False(DiscoveryProtocol.IsValid(Secret, beacon, Now));
    }

    [Fact]
    public void Non_hex_signature_is_rejected_without_exception()
    {
        DiscoveryBeacon beacon = SignedBeacon();
        beacon.Signature = "zz-not-hex";

        Assert.False(DiscoveryProtocol.IsValid(Secret, beacon, Now));
    }
}
