using System.Security.Cryptography;
using System.Text;

namespace TimeGuard.Core;

/// <summary>
/// Генерация и проверка коротких ЦИФРОВЫХ кодов, привязанных ко времени
/// (TOTP-подобных). Счётчик не используется: любой родитель с тем же секретом
/// генерирует валидный код независимо, без общего состояния.
///
/// Формат — 9 цифр, отображается как XXX-XXX-XXX:
///   первые 3 цифры  = минуты (0..999),
///   последние 6 цифр = подпись = HMAC-SHA256(secret, machineId||минуты||шаг) mod 10^6.
///
/// Шаг времени = unixSeconds / <see cref="PeriodSeconds"/> и в код НЕ пишется:
/// клиент перебирает шаги в окне ±<see cref="WindowSteps"/> вокруг своего времени.
/// Код привязан к machineId (в подписи), поэтому не подойдёт другой паре.
/// </summary>
public sealed class TimeGuardCodec
{
    /// <summary>Длительность одного временного шага в секундах.</summary>
    public const int PeriodSeconds = 60;

    /// <summary>
    /// Допуск в шагах в обе стороны. При шаге 60 c значение 10 даёт около
    /// 10 минут прямого срока жизни кода и до ±10 минут терпимости к часам.
    /// </summary>
    public const int WindowSteps = 10;

    /// <summary>Максимум минут в коде (3 цифры).</summary>
    public const int MaxMinutes = 999;

    private const int MacDigits = 6;
    private const int MacMod = 1_000_000;

    private readonly byte[] _secret;
    private readonly byte[] _machineIdBytes;

    public TimeGuardCodec(byte[] secret, string machineId)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentException.ThrowIfNullOrEmpty(machineId);
        if (secret.Length < 16)
        {
            throw new ArgumentException("Секрет должен быть не короче 16 байт.", nameof(secret));
        }

        _secret = (byte[])secret.Clone();
        _machineIdBytes = Encoding.UTF8.GetBytes(machineId);
    }

    /// <summary>Сгенерировать код на текущее время (сторона родителя).</summary>
    public string Generate(int minutes, long unixSeconds)
    {
        if (minutes is < 0 or > MaxMinutes)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), $"Минуты 0..{MaxMinutes}.");
        }

        long step = unixSeconds / PeriodSeconds;
        int mac = Mac6(minutes, step);
        string digits = minutes.ToString("D3") + mac.ToString("D6");
        return Group(digits);
    }

    /// <summary>Максимум резервных кодов в наборе (индекс 0..9).</summary>
    public const int BackupCount = 10;

    /// <summary>
    /// Сгенерировать РЕЗЕРВНЫЙ код (10 цифр): без срока действия, одноразовый.
    /// Детерминирован: один и тот же индекс+минуты дают один и тот же код, поэтому
    /// набор одинаков на любом родительском ПК. Формат: индекс(1)+минуты(3)+подпись(6).
    /// </summary>
    public string GenerateBackup(int index, int minutes)
    {
        if (index is < 0 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Индекс 0..9.");
        }
        if (minutes is < 0 or > MaxMinutes)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), $"Минуты 0..{MaxMinutes}.");
        }
        int mac = MacBackup(minutes, index);
        string digits = index.ToString("D1") + minutes.ToString("D3") + mac.ToString("D6");
        return Group(digits);
    }

    /// <summary>
    /// Проверить резервный код (10 цифр). Возвращает true и минуты при верной подписи.
    /// Защита от повтора — на стороне вызывающего (по <paramref name="tag"/>).
    /// </summary>
    public bool VerifyBackup(string code, out int minutes, out string tag)
    {
        minutes = 0;
        tag = string.Empty;
        string digits = OnlyDigits(code);
        if (digits.Length != 10)
        {
            return false;
        }
        int index = digits[0] - '0';
        int mins = int.Parse(digits.Substring(1, 3));
        int mac = int.Parse(digits.Substring(4, MacDigits));
        if (MacBackup(mins, index) != mac)
        {
            return false;
        }
        minutes = mins;
        tag = digits;
        return true;
    }

    /// <summary>
    /// Проверить код (сторона клиента) на текущее время. Защита от повтора — не
    /// здесь: вызывающий хранит принятые <see cref="CodeVerifyResult.Tag"/>.
    /// </summary>
    public CodeVerifyResult Verify(string code, long unixSeconds)
    {
        string digits = OnlyDigits(code);
        if (digits.Length != 9)
        {
            return new CodeVerifyResult(CodeVerifyStatus.MalformedCode, default, string.Empty, 0);
        }

        int minutes = int.Parse(digits[..3]);
        int mac = int.Parse(digits.Substring(3, MacDigits));

        long nowStep = unixSeconds / PeriodSeconds;
        for (long d = -WindowSteps; d <= WindowSteps; d++)
        {
            long step = nowStep + d;
            if (step < 0)
            {
                continue;
            }
            if (Mac6(minutes, step) == mac)
            {
                long expireAt = (step + WindowSteps + 1) * PeriodSeconds;
                return new CodeVerifyResult(
                    CodeVerifyStatus.Ok, new CodePayload(minutes), digits, expireAt);
            }
        }

        return new CodeVerifyResult(CodeVerifyStatus.BadSignature, default, string.Empty, 0);
    }

    private int Mac6(int minutes, long step)
    {
        Span<byte> message = stackalloc byte[_machineIdBytes.Length + 2 + 8];
        _machineIdBytes.CopyTo(message);
        int o = _machineIdBytes.Length;
        message[o] = (byte)(minutes >> 8);
        message[o + 1] = (byte)minutes;
        o += 2;
        for (int i = 7; i >= 0; i--)
        {
            message[o + i] = (byte)(step & 0xFF);
            step >>= 8;
        }

        Span<byte> full = stackalloc byte[32];
        HMACSHA256.HashData(_secret, message, full);
        uint v = ((uint)full[0] << 24) | ((uint)full[1] << 16) | ((uint)full[2] << 8) | full[3];
        return (int)(v % MacMod);
    }

    // Подпись резервного кода: без времени, с доменным разделителем (0xFF),
    // чтобы не совпадала с временными кодами.
    private int MacBackup(int minutes, int index)
    {
        Span<byte> message = stackalloc byte[_machineIdBytes.Length + 1 + 2 + 1];
        _machineIdBytes.CopyTo(message);
        int o = _machineIdBytes.Length;
        message[o++] = 0xFF;
        message[o++] = (byte)(minutes >> 8);
        message[o++] = (byte)minutes;
        message[o] = (byte)index;

        Span<byte> full = stackalloc byte[32];
        HMACSHA256.HashData(_secret, message, full);
        uint v = ((uint)full[0] << 24) | ((uint)full[1] << 16) | ((uint)full[2] << 8) | full[3];
        return (int)(v % MacMod);
    }

    private static string OnlyDigits(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return string.Empty;
        }
        var sb = new StringBuilder(code.Length);
        foreach (char c in code)
        {
            if (c is >= '0' and <= '9')
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    // Дефисы после 3-й и 6-й цифры: 9 цифр -> XXX-XXX-XXX, 10 -> XXX-XXX-XXXX.
    private static string Group(string digits)
    {
        var sb = new StringBuilder(digits.Length + 2);
        for (int i = 0; i < digits.Length; i++)
        {
            if (i is 3 or 6)
            {
                sb.Append('-');
            }
            sb.Append(digits[i]);
        }
        return sb.ToString();
    }
}
