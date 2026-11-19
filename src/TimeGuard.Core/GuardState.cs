namespace TimeGuard.Core;

/// <summary>
/// Изменяемое состояние времени. Авторитетно хранится службой и шифруется DPAPI.
/// </summary>
public sealed class GuardState
{
    /// <summary>Оставшийся бюджет времени в секундах. 0 — экран должен быть заблокирован.</summary>
    public long RemainingSeconds { get; set; }

    /// <summary>
    /// Использованные коды (тег → момент истечения, unix-секунды) для защиты от
    /// повтора в пределах временного окна. Просроченные записи периодически чистятся.
    /// </summary>
    public Dictionary<string, long> ConsumedCodes { get; set; } = new();

    /// <summary>
    /// Использованные РЕЗЕРВНЫЕ коды (без срока). Хранятся постоянно, каждый
    /// резервный код срабатывает один раз.
    /// </summary>
    public List<string> UsedBackupCodes { get; set; } = new();

    /// <summary>Экран сейчас разблокирован пользователем? (иначе показывается заглушка).</summary>
    public bool IsUnlocked { get; set; }

    /// <summary>
    /// Снят ли ночной запрет мастер-паролем на текущую ночь. Сбрасывается,
    /// когда ночной период заканчивается.
    /// </summary>
    public bool CurfewOverridden { get; set; }

    /// <summary>UTC последнего сохранения состояния (для аудита/диагностики).</summary>
    public DateTimeOffset SavedAtUtc { get; set; }
}

/// <summary>
/// Неизменяемая конфигурация, задаётся при установке пары родитель↔клиент.
/// Секрет и хэш мастер-пароля хранятся зашифрованными (DPAPI LocalMachine).
/// </summary>
public sealed class GuardConfig
{
    /// <summary>Общий HMAC-секрет (Base64). Идентичен у родителя и клиента.</summary>
    public string SecretBase64 { get; set; } = string.Empty;

    /// <summary>Идентификатор клиентской машины, вплетаемый в подпись кодов.</summary>
    public string MachineId { get; set; } = string.Empty;

    /// <summary>PBKDF2-хэш мастер-пароля (Base64).</summary>
    public string MasterHashBase64 { get; set; } = string.Empty;

    /// <summary>Соль мастер-пароля (Base64).</summary>
    public string MasterSaltBase64 { get; set; } = string.Empty;

    /// <summary>
    /// Учётка, к которой применяется ограничение (имя без домена). Пусто —
    /// ограничивать любого вошедшего (обратная совместимость). Задаётся при установке.
    /// </summary>
    public string TargetUser { get; set; } = string.Empty;

    /// <summary>Включён ли ночной запрет (комендантский час).</summary>
    public bool CurfewEnabled { get; set; } = true;

    /// <summary>Час начала ночного запрета (локальное время, 0..23). По умолчанию 21.</summary>
    public int CurfewStartHour { get; set; } = 21;

    /// <summary>Час окончания ночного запрета (локальное время, 0..23). По умолчанию 9.</summary>
    public int CurfewEndHour { get; set; } = 9;

    public byte[] GetSecret() => Convert.FromBase64String(SecretBase64);
}
