using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace TimeGuard.Service.Interop;

/// <summary>
/// Применяет и поддерживает политики, затрудняющие обход: отключение Диспетчера
/// задач для конкретной учётки ребёнка (по её SID в кусте HKEY_USERS).
/// Только для этого пользователя — админ-родитель не затрагивается.
/// Служба переустанавливает значения периодически, поэтому ручной сброс из-под
/// ограниченной учётки бесполезен (нет прав на политику).
/// </summary>
public sealed class PolicyGuard(ILogger<PolicyGuard> log)
{
    private const string PolicySubKey =
        @"Software\Microsoft\Windows\CurrentVersion\Policies\System";

    public void Apply(string userSid)
    {
        if (string.IsNullOrEmpty(userSid))
        {
            return;
        }
        try
        {
            string path = $@"{userSid}\{PolicySubKey}";
            using RegistryKey key = Registry.Users.CreateSubKey(path, writable: true);
            key.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Не удалось применить политику для {Sid}.", userSid);
        }
    }

    public void Remove(string userSid)
    {
        if (string.IsNullOrEmpty(userSid))
        {
            return;
        }
        try
        {
            string path = $@"{userSid}\{PolicySubKey}";
            using RegistryKey? key = Registry.Users.OpenSubKey(path, writable: true);
            key?.DeleteValue("DisableTaskMgr", throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Не удалось снять политику для {Sid}.", userSid);
        }
    }
}
