using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TimeGuard.Agent;

/// <summary>
/// Низкоуровневый клавиатурный хук (WH_KEYBOARD_LL), подавляющий комбинации
/// ухода с оверлея: Win, Alt+Tab, Alt+Esc, Ctrl+Esc, Alt+F4, Win+любая.
/// Ограничение Windows: Ctrl+Alt+Del (Secure Attention Sequence) перехватить из
/// пользовательского процесса нельзя — это делается только политиками/драйвером.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_F4 = 0x73;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hook = IntPtr.Zero;
    private volatile bool _active;

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    /// <summary>Включить перехват (когда показан оверлей).</summary>
    public void Enable()
    {
        _active = true;
        if (_hook != IntPtr.Zero)
        {
            return;
        }
        using Process cur = Process.GetCurrentProcess();
        using ProcessModule mod = cur.MainModule!;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(mod.ModuleName), 0);
    }

    /// <summary>Отключить перехват (оверлей скрыт).</summary>
    public void Disable() => _active = false;

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _active && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            int vk = Marshal.ReadInt32(lParam);
            if (ShouldBlock(vk))
            {
                return (IntPtr)1; // проглотить нажатие
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static bool ShouldBlock(int vk)
    {
        bool alt = IsDown(VK_MENU);
        bool ctrl = IsDown(VK_CONTROL);
        bool win = IsDown(VK_LWIN) || IsDown(VK_RWIN);

        if (vk is VK_LWIN or VK_RWIN)
        {
            return true;
        }
        if (win)
        {
            return true; // Win+любая
        }
        if (alt && vk is VK_TAB or VK_ESCAPE or VK_F4)
        {
            return true;
        }
        if (ctrl && vk == VK_ESCAPE)
        {
            return true;
        }
        return false;
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        _active = false;
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
