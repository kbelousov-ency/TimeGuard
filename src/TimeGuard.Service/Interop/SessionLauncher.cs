using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using static TimeGuard.Service.Interop.NativeMethods;

namespace TimeGuard.Service.Interop;

/// <summary>
/// Запуск UI-агента в интерактивной сессии активного пользователя из-под
/// службы SYSTEM (WTSQueryUserToken + CreateProcessAsUser). За счёт этого агент
/// не зависит от папки «Автозагрузка» и стартует под контролем службы.
/// </summary>
public sealed class SessionLauncher(ILogger<SessionLauncher> log)
{
    private const uint NoActiveSession = 0xFFFFFFFF;

    /// <summary>Есть ли сейчас активная пользовательская консольная сессия.</summary>
    /// <summary>Id активной консольной сессии (0xFFFFFFFF, если сессии нет).</summary>
    public uint GetActiveConsoleSessionId() => WTSGetActiveConsoleSessionId();

    public bool HasActiveUserSession()
    {
        uint sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == NoActiveSession)
        {
            return false;
        }
        if (!WTSQueryUserToken(sessionId, out IntPtr token))
        {
            return false;
        }
        CloseHandle(token);
        return true;
    }

    /// <summary>Сессия подключена и Windows разблокирована (Windows 10/11).</summary>
    public bool IsActiveSessionUnlocked()
    {
        uint sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == NoActiveSession)
        {
            return false;
        }
        IntPtr buffer = IntPtr.Zero;
        try
        {
            const int WTSSessionInfoEx = 25;
            if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, WTSSessionInfoEx,
                    out buffer, out uint bytes) || buffer == IntPtr.Zero ||
                bytes < Marshal.SizeOf<WTSINFOEX_PREFIX>())
            {
                return false;
            }
            var info = Marshal.PtrToStructure<WTSINFOEX_PREFIX>(buffer);
            // WTSActive = 0; WTS_SESSIONSTATE_UNLOCK = 1. Unknown means no charge.
            return info.Level == 1 && info.SessionId == sessionId &&
                info.SessionState == 0 && info.SessionFlags == 1;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                WTSFreeMemory(buffer);
            }
        }
    }

    /// <summary>
    /// Запустить процесс в активной сессии. Возвращает PID или null, если сессии нет.
    /// </summary>
    public int? LaunchInActiveSession(string exePath)
    {
        uint sessionId = WTSGetActiveConsoleSessionId();
        log.LogInformation("Запуск агента: активная консольная сессия = {Session}.", sessionId);
        if (sessionId == NoActiveSession)
        {
            log.LogWarning("Нет активной консольной сессии (WTSGetActiveConsoleSessionId=0xFFFFFFFF).");
            return null;
        }

        if (!WTSQueryUserToken(sessionId, out IntPtr userToken))
        {
            log.LogWarning("WTSQueryUserToken не удался для сессии {Session}, код ошибки {Err}. " +
                "Проверьте, что служба работает как LocalSystem и пользователь вошёл.",
                sessionId, Marshal.GetLastWin32Error());
            return null;
        }

        IntPtr primaryToken = IntPtr.Zero;
        IntPtr envBlock = IntPtr.Zero;
        try
        {
            if (!DuplicateTokenEx(userToken, TOKEN_ALL_ACCESS, IntPtr.Zero,
                    SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TOKEN_TYPE.TokenPrimary, out primaryToken))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx");
            }

            CreateEnvironmentBlock(out envBlock, primaryToken, false);

            var si = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop = @"winsta0\default",
            };

            // Командная строка ОБЯЗАТЕЛЬНА: без argv[0] апп-хост .NET не находит себя
            // и завершается до управляемого кода. Первый аргумент — путь к exe.
            var commandLine = new System.Text.StringBuilder($"\"{exePath}\"");

            bool ok = CreateProcessAsUser(
                primaryToken,
                exePath,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CREATE_UNICODE_ENVIRONMENT | NORMAL_PRIORITY_CLASS,
                envBlock,
                Path.GetDirectoryName(exePath),
                ref si,
                out PROCESS_INFORMATION pi);

            if (!ok)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessAsUser");
            }

            CloseHandle(pi.hThread);
            log.LogInformation("Агент создан в сессии {Session}, PID {Pid}. Жду 3 c для проверки...",
                sessionId, pi.dwProcessId);

            // Диагностика: подождать и посмотреть, не завершился ли агент сразу.
            uint wait = WaitForSingleObject(pi.hProcess, 3000);
            if (wait == 0 && GetExitCodeProcess(pi.hProcess, out uint code))
            {
                log.LogWarning("Агент завершился за 3 c с кодом 0x{Code:X8} ({Dec}).", code, unchecked((int)code));
            }
            else
            {
                log.LogInformation("Агент через 3 c ещё работает (PID {Pid}).", pi.dwProcessId);
            }

            CloseHandle(pi.hProcess);
            return pi.dwProcessId;
        }
        finally
        {
            if (envBlock != IntPtr.Zero)
            {
                DestroyEnvironmentBlock(envBlock);
            }
            if (primaryToken != IntPtr.Zero)
            {
                CloseHandle(primaryToken);
            }
            CloseHandle(userToken);
        }
    }
}


/// <summary>Расширение: получить SID пользователя активной сессии.</summary>
public static class ActiveSessionUser
{
    public static string? GetActiveSessionUserSid()
    {
        uint sessionId = NativeMethods.WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF)
        {
            return null;
        }
        if (!NativeMethods.WTSQueryUserToken(sessionId, out IntPtr token))
        {
            return null;
        }
        try
        {
            using var identity = new System.Security.Principal.WindowsIdentity(token);
            return identity.User?.Value;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }

    /// <summary>Имя учётки активной сессии без домена (например "Roma"), или null.</summary>
    public static string? GetActiveSessionUserName()
    {
        uint sessionId = NativeMethods.WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF)
        {
            return null;
        }
        if (!NativeMethods.WTSQueryUserToken(sessionId, out IntPtr token))
        {
            return null;
        }
        try
        {
            using var identity = new System.Security.Principal.WindowsIdentity(token);
            string? full = identity.Name; // "МАШИНА\Имя"
            if (string.IsNullOrEmpty(full))
            {
                return null;
            }
            int slash = full.LastIndexOf('\\');
            return slash >= 0 ? full[(slash + 1)..] : full;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }
}
