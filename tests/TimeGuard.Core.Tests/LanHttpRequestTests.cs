using System.Text;
using TimeGuard.Core;
using Xunit;

namespace TimeGuard.Core.Tests;

public sealed class LanHttpRequestTests
{
    private static Task<LanHttpRequest?> Parse(byte[] raw) =>
        LanHttpRequest.ReadAsync(new MemoryStream(raw), CancellationToken.None);

    private static byte[] Raw(string head, byte[] body) =>
        Encoding.ASCII.GetBytes(head).Concat(body).ToArray();

    [Fact]
    public async Task Body_is_read_as_utf8_bytes_by_content_length()
    {
        byte[] body = Encoding.UTF8.GetBytes("{\"reason\":\"Хочу доиграть\"}");
        LanHttpRequest? request = await Parse(Raw(
            $"POST /timeguard/request-time HTTP/1.1\r\nHost: parent\r\nContent-Length: {body.Length}\r\n\r\n", body));

        Assert.NotNull(request);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/timeguard/request-time", request.Target);
        Assert.Equal("parent", request.Headers["host"]);
        Assert.Equal(body, request.Body);
        Assert.Contains("Хочу доиграть", request.BodyText);
    }

    [Fact]
    public async Task Request_without_content_length_has_empty_body()
    {
        LanHttpRequest? request = await Parse(Raw("GET /timeguard/history?limit=5 HTTP/1.1\r\n\r\n", []));

        Assert.NotNull(request);
        Assert.Empty(request.Body);
    }

    [Fact]
    public async Task Too_large_content_length_is_413()
    {
        var ex = await Assert.ThrowsAsync<LanHttpException>(() => Parse(Raw(
            $"POST /timeguard/control HTTP/1.1\r\nContent-Length: {HistoryProtocol.MaxBodyBytes + 1}\r\n\r\n", [])));
        Assert.Equal(413, ex.StatusCode);
    }

    [Fact]
    public async Task Too_long_header_line_is_rejected()
    {
        string longHeader = "X-Junk: " + new string('a', HistoryProtocol.MaxHeaderLineBytes);
        var ex = await Assert.ThrowsAsync<LanHttpException>(() => Parse(Raw(
            $"GET /timeguard/history HTTP/1.1\r\n{longHeader}\r\n\r\n", [])));
        Assert.Equal(431, ex.StatusCode);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    public async Task Invalid_content_length_is_400(string length)
    {
        var ex = await Assert.ThrowsAsync<LanHttpException>(() => Parse(Raw(
            $"POST /timeguard/control HTTP/1.1\r\nContent-Length: {length}\r\n\r\n", [])));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Real_http_client_request_parses_and_authenticates_over_socket()
    {
        byte[] secret = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        byte[] body = Encoding.UTF8.GetBytes("{\"minutes\":30,\"reason\":\"Доделать уроки\"}");
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            Task<bool> server = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                LanHttpRequest? request = await LanHttpRequest.ReadAsync(stream, CancellationToken.None);
                bool ok = request is not null && request.BodyText.Contains("Доделать уроки") &&
                    HistoryProtocol.Authenticate(request, secret, "child-pc", HistoryProtocol.ParentRequestRoute,
                        new ReplayGuard(), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
                return ok;
            });

            using var message = HistoryProtocol.CreateSignedRequest(
                System.Net.Http.HttpMethod.Post, new Uri($"http://127.0.0.1:{port}{HistoryProtocol.ParentRequestRoute}"),
                secret, "child-pc", HistoryProtocol.ParentRequestRoute, body);
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await http.SendAsync(message);

            Assert.True(await server);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Truncated_body_is_400()
    {
        var ex = await Assert.ThrowsAsync<LanHttpException>(() => Parse(Raw(
            "POST /timeguard/control HTTP/1.1\r\nContent-Length: 10\r\n\r\n", "abc"u8.ToArray())));
        Assert.Equal(400, ex.StatusCode);
    }
}
