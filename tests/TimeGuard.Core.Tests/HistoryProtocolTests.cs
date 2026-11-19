using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using TimeGuard.Core;
using Xunit;

namespace TimeGuard.Core.Tests;

public sealed class HistoryProtocolTests
{
    private const string Machine = "child-pc";
    private const long Timestamp = 1_700_000_000;
    private const string Nonce = "0123456789abcdef0123456789abcdef";
    private static readonly byte[] Secret = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] StatusBody = Encoding.UTF8.GetBytes("{\"action\":\"status\",\"minutes\":0}");
    private static readonly byte[] AddBody = Encoding.UTF8.GetBytes("{\"action\":\"add\",\"minutes\":999}");

    private static string SignControl(byte[] body) =>
        HistoryProtocol.Sign(Secret, Machine, Timestamp, Nonce, HistoryProtocol.ControlRoute, body);

    private static bool IsValid(string signature, byte[] body, long now = Timestamp, string machine = Machine,
        string route = HistoryProtocol.ControlRoute, string nonce = Nonce) =>
        HistoryProtocol.IsValidSignature(Secret, machine, Timestamp, nonce, signature, now, route, body);

    [Fact]
    public void Signature_accepts_matching_request_and_rejects_tampering()
    {
        string signature = SignControl(StatusBody);

        Assert.True(IsValid(signature, StatusBody));
        Assert.False(IsValid(signature, StatusBody, machine: "other-pc"));
        Assert.False(IsValid(signature, StatusBody, route: HistoryProtocol.Route));
        Assert.False(IsValid(signature, StatusBody, nonce: "fedcba9876543210fedcba9876543210"));
    }

    [Fact]
    public void Signature_depends_on_body()
    {
        Assert.NotEqual(SignControl(StatusBody), SignControl(AddBody));
        Assert.NotEqual(SignControl([]), SignControl(StatusBody));
    }

    [Fact]
    public void Changed_body_does_not_pass()
    {
        string signature = SignControl(StatusBody);

        Assert.False(IsValid(signature, AddBody));
        Assert.False(IsValid(signature, []));
        byte[] flipped = (byte[])StatusBody.Clone();
        flipped[^2] ^= 1;
        Assert.False(IsValid(signature, flipped));
    }

    [Fact]
    public void Empty_body_is_signed_as_hash_of_empty_string()
    {
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", HistoryProtocol.HashBody([]));
        string signature = HistoryProtocol.Sign(Secret, Machine, Timestamp, Nonce, HistoryProtocol.Route, []);
        Assert.True(IsValid(signature, [], route: HistoryProtocol.Route));
    }

    [Fact]
    public void Expired_or_future_timestamp_is_rejected()
    {
        string signature = SignControl(StatusBody);

        Assert.True(IsValid(signature, StatusBody, now: Timestamp + HistoryProtocol.TimestampWindowSeconds));
        Assert.False(IsValid(signature, StatusBody, now: Timestamp + HistoryProtocol.TimestampWindowSeconds + 1));
        Assert.False(IsValid(signature, StatusBody, now: Timestamp - HistoryProtocol.TimestampWindowSeconds - 1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-hex")]
    [InlineData("abc\nchild-pc")]
    public void Malformed_nonce_is_rejected(string nonce)
    {
        string signature = HistoryProtocol.Sign(Secret, Machine, Timestamp, nonce, HistoryProtocol.ControlRoute, StatusBody);
        Assert.False(IsValid(signature, StatusBody, nonce: nonce));
    }

    [Fact]
    public async Task Signed_request_authenticates_once_and_replay_is_rejected()
    {
        using HttpRequestMessage message = HistoryProtocol.CreateSignedRequest(
            HttpMethod.Post, new Uri("http://child/timeguard/control"), Secret, Machine, HistoryProtocol.ControlRoute, AddBody);
        byte[] sentBody = await message.Content!.ReadAsByteArrayAsync();
        Assert.Equal(AddBody, sentBody);

        LanHttpRequest request = ToLanRequest(message, sentBody);
        long now = long.Parse(request.Headers[HistoryProtocol.TimestampHeader]);
        var replay = new ReplayGuard();

        Assert.True(HistoryProtocol.Authenticate(request, Secret, Machine, HistoryProtocol.ControlRoute, replay, now));
        Assert.False(HistoryProtocol.Authenticate(request, Secret, Machine, HistoryProtocol.ControlRoute, replay, now + 1));
    }

    [Fact]
    public async Task Captured_headers_with_other_body_fail_authentication()
    {
        using HttpRequestMessage message = HistoryProtocol.CreateSignedRequest(
            HttpMethod.Post, new Uri("http://child/timeguard/control"), Secret, Machine, HistoryProtocol.ControlRoute, StatusBody);
        LanHttpRequest captured = ToLanRequest(message, await message.Content!.ReadAsByteArrayAsync());
        long now = long.Parse(captured.Headers[HistoryProtocol.TimestampHeader]);
        var forged = new LanHttpRequest(captured.Method, captured.Target, captured.Headers, AddBody);
        var replay = new ReplayGuard();

        Assert.False(HistoryProtocol.Authenticate(forged, Secret, Machine, HistoryProtocol.ControlRoute, replay, now));
        Assert.Equal(0, replay.Count);
    }

    [Fact]
    public void Two_signed_requests_with_same_body_in_same_second_differ()
    {
        using HttpRequestMessage first = HistoryProtocol.CreateSignedRequest(
            HttpMethod.Get, new Uri("http://child/timeguard/history"), Secret, Machine, HistoryProtocol.Route, null);
        using HttpRequestMessage second = HistoryProtocol.CreateSignedRequest(
            HttpMethod.Get, new Uri("http://child/timeguard/history"), Secret, Machine, HistoryProtocol.Route, null);

        Assert.Null(first.Content);
        Assert.NotEqual(Header(first, HistoryProtocol.SignatureHeader), Header(second, HistoryProtocol.SignatureHeader));
    }

    private static string Header(HttpRequestMessage message, string name) => message.Headers.GetValues(name).Single();

    private static LanHttpRequest ToLanRequest(HttpRequestMessage message, byte[] body)
    {
        var headers = message.Headers.ToDictionary(h => h.Key, h => h.Value.Single(), StringComparer.OrdinalIgnoreCase);
        return new LanHttpRequest(message.Method.Method, message.RequestUri!.PathAndQuery, headers, body);
    }
}
