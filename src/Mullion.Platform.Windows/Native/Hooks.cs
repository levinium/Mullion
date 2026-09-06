using System.Runtime.InteropServices;

namespace Mullion.Platform.Windows.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct KBDLLHOOKSTRUCT
{
    public uint vkCode;
    public uint scanCode;
    public uint flags;
    public uint time;
    public nuint dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public nuint dwExtraInfo;
}

[StructLayout(LayoutKind.Explicit)]
internal struct INPUTUNION
{
    [FieldOffset(0)] public KEYBDINPUT ki;

    // MOUSEINPUT is the largest member (24 bytes on x64); pad so INPUT sizes
    // correctly even though only the keyboard variant is ever used.
    [FieldOffset(0)] public MOUSEINPUT mi;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public nuint dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    public uint type;
    public INPUTUNION u;
}

[StructLayout(LayoutKind.Sequential)]
internal struct LASTINPUTINFO
{
    public uint cbSize;
    public uint dwTime;
}

internal static partial class Hooks
{
    internal const int WH_KEYBOARD_LL = 13;
    internal const int HC_ACTION = 0;

    internal const uint WM_KEYDOWN = 0x0100;
    internal const uint WM_KEYUP = 0x0101;
    internal const uint WM_SYSKEYDOWN = 0x0104;
    internal const uint WM_SYSKEYUP = 0x0105;

    internal const uint LLKHF_EXTENDED = 0x01;
    internal const uint LLKHF_INJECTED = 0x10;
    internal const uint LLKHF_UP = 0x80;

    internal const uint INPUT_KEYBOARD = 1;
    internal const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>
    /// VK__none_ - a key that produces no text and maps to no command, but which
    /// the shell still counts as "a key was pressed while Win was held", which is
    /// what stops the Start menu opening on release.
    /// </summary>
    internal const ushort VK_NONE = 0xFF;

    /// <summary>"MULN" - tags every event we inject so we never reprocess our own.</summary>
    internal const nuint SentinelExtraInfo = 0x4D554C4E;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint HookProc(int nCode, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowsHookExW")]
    internal static partial nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hmod, uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    internal static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint SendInput(uint cInputs, [In] INPUT[] pInputs, int cbSize);

    [LibraryImport("user32.dll")]
    internal static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetLastInputInfo(ref LASTINPUTINFO info);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint hwnd, int id);
}
