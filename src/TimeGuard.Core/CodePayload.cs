namespace TimeGuard.Core;

/// <summary>Раскодированное содержимое цифрового кода.</summary>
/// <param name="Minutes">Количество минут, выдаваемых кодом.</param>
public readonly record struct CodePayload(int Minutes);

/// <summary>Результат проверки подписи и временного окна кода клиентом.</summary>
public enum CodeVerifyStatus
{
    Ok,
    MalformedCode,
    BadSignature,   // подпись неверна или окно времени не совпало
}

/// <param name="Status">Итог проверки.</param>
/// <param name="Payload">Содержимое при <see cref="CodeVerifyStatus.Ok"/>.</param>
/// <param name="Tag">Уникальный ключ кода для защиты от повтора (нормализованные цифры).</param>
/// <param name="ExpireAtUnix">Момент, после которого код заведомо невалиден.</param>
public readonly record struct CodeVerifyResult(
    CodeVerifyStatus Status, CodePayload Payload, string Tag, long ExpireAtUnix)
{
    public bool IsValid => Status == CodeVerifyStatus.Ok;
}
