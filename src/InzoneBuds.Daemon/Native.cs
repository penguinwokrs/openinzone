using System.Runtime.InteropServices;

namespace InzoneBuds.Daemon;

[StructLayout(LayoutKind.Sequential)]
internal struct Msg
{
    public IntPtr Hwnd;
    public uint Message;
    public IntPtr WParam;
    public IntPtr LParam;
    public uint Time;
    public int PtX;
    public int PtY;
}

internal static class Native
{
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    /// <summary>Stops auto-repeat from firing the action once per keyboard repeat tick.</summary>
    public const uint MOD_NOREPEAT = 0x4000;

    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_QUIT = 0x0012;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetMessageW(out Msg msg, IntPtr hWnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostThreadMessageW(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();
}
