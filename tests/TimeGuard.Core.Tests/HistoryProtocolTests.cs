using System.Security.Cryptography;
using TimeGuard.Core;
using Xunit;

namespace TimeGuard.Core.Tests;

public sealed class HistoryProtocolTests
{
    [Fact]
    public void Signature_accepts_matching_request_and_rejects_tampering()
    {
        byte[] secret = RandomNumberGenerator.GetBytes(32);
        const string machine = "child-pc";
        const long timestamp = 1_700_000_000;
        string signature = HistoryProtocol.Sign(secret, machine, timestamp);

        Assert.True(HistoryProtocol.IsValidSignature(secret, machine, timestamp, signature, timestamp));
        Assert.False(HistoryProtocol.IsValidSignature(secret, "other-pc", timestamp, signature, timestamp));
        Assert.False(HistoryProtocol.IsValidSignature(secret, machine, timestamp + 301, signature, timestamp));
    }
}
