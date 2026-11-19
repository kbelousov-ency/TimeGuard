using System.Globalization;
using System.Text;

namespace TimeGuard.Core;

/// <summary>Ошибка разбора LAN-запроса, на которую сервер отвечает указанным HTTP-кодом.</summary>
public sealed class LanHttpException(int statusCode, string reason) : Exception(reason)
{
    public int StatusCode { get; } = statusCode;
    public string Reason { get; } = reason;
}

/// <summary>
/// Минимальный разбор HTTP/1.1-запроса LAN-протокола: строка запроса, заголовки
/// (длина строки ограничена <see cref="HistoryProtocol.MaxHeaderLineBytes"/>) и тело байтами по Content-Length
/// (не больше <see cref="HistoryProtocol.MaxBodyBytes"/>).
/// </summary>
public sealed class LanHttpRequest
{
    public const int MaxHeaders = 32;

    public LanHttpRequest(string method, string target, IReadOnlyDictionary<string, string> headers, byte[] body)
    {
        Method = method;
        Target = target;
        Headers = headers;
        Body = body;
    }

    public string Method { get; }
    public string Target { get; }
    public IReadOnlyDictionary<string, string> Headers { get; }
    public byte[] Body { get; }
    public string BodyText => Encoding.UTF8.GetString(Body);

    /// <summary>Читает запрос; null — соединение закрыто или пустая строка запроса.</summary>
    /// <exception cref="LanHttpException">Запрос некорректен или превышает лимиты.</exception>
    public static async Task<LanHttpRequest?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var reader = new BufferedReader(stream);
        string? requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return null;
        }
        string[] parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
        {
            throw new LanHttpException(400, "Bad Request");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int count = 0; ; count++)
        {
            string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null || line.Length == 0)
            {
                break;
            }
            if (count >= MaxHeaders)
            {
                throw new LanHttpException(431, "Request Header Fields Too Large");
            }
            int separator = line.IndexOf(':');
            if (separator > 0)
            {
                headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        if (headers.ContainsKey("Transfer-Encoding"))
        {
            throw new LanHttpException(411, "Length Required");
        }
        int length = 0;
        if (headers.TryGetValue("Content-Length", out string? lengthText))
        {
            if (!long.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out long declared))
            {
                throw new LanHttpException(400, "Bad Request");
            }
            if (declared > HistoryProtocol.MaxBodyBytes)
            {
                throw new LanHttpException(413, "Payload Too Large");
            }
            length = (int)declared;
        }

        byte[] body = new byte[length];
        await reader.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        return new LanHttpRequest(parts[0], parts[1], headers, body);
    }

    private sealed class BufferedReader(Stream stream)
    {
        private readonly byte[] _buffer = new byte[4096];
        private int _position;
        private int _length;

        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            var line = new List<byte>();
            while (true)
            {
                if (_position == _length && !await FillAsync(cancellationToken).ConfigureAwait(false))
                {
                    return line.Count == 0 ? null : Decode(line);
                }
                byte value = _buffer[_position++];
                if (value == (byte)'\n')
                {
                    return Decode(line);
                }
                if (line.Count >= HistoryProtocol.MaxHeaderLineBytes)
                {
                    throw new LanHttpException(431, "Request Header Fields Too Large");
                }
                line.Add(value);
            }
        }

        public async Task ReadExactlyAsync(byte[] destination, CancellationToken cancellationToken)
        {
            int buffered = Math.Min(_length - _position, destination.Length);
            Array.Copy(_buffer, _position, destination, 0, buffered);
            _position += buffered;
            try
            {
                await stream.ReadExactlyAsync(destination.AsMemory(buffered), cancellationToken).ConfigureAwait(false);
            }
            catch (EndOfStreamException)
            {
                throw new LanHttpException(400, "Bad Request");
            }
        }

        private async Task<bool> FillAsync(CancellationToken cancellationToken)
        {
            _position = 0;
            _length = await stream.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);
            return _length > 0;
        }

        private static string Decode(List<byte> line)
        {
            int count = line.Count > 0 && line[^1] == (byte)'\r' ? line.Count - 1 : line.Count;
            return Encoding.ASCII.GetString(line.ToArray(), 0, count);
        }
    }
}
