using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mullion.Platform.Windows.Native;

namespace Mullion.Platform.Windows.Testing;

/// <summary>
/// Synthesises key events for testing the hook end to end.
/// <para>
/// Deliberately does NOT carry Mullion's sentinel in dwExtraInfo, so the state
/// machine treats these as genuine third-party input rather than filtering them
/// as its own injection. That makes this a real test of the matching path.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class KeyInjector
{
    private const nuint TestExtraInfo = 0x54455354;   // "TEST"

    public const ushort VkLWin = 0x5B;

    /// <summary>
    /// Exposed so a test can detect the Start menu opening: it takes foreground
    /// when it appears, which is the only automated signal that Win-key
    /// suppression failed.
    /// </summary>
    public static nint ForegroundWindow() => Win.GetForegroundWindow();

    public static void Press(ushort virtualKey, ushort scanCode)
    {
        Send(virtualKey, scanCode, keyUp: false);
        Thread.Sleep(20);
        Send(virtualKey, scanCode, keyUp: true);
    }

    public static void KeyDown(ushort virtualKey, ushort scanCode) => Send(virtualKey, scanCode, false);

    public static void KeyUp(ushort virtualKey, ushort scanCode) => Send(virtualKey, scanCode, true);

    /// <summary>Hold <paramref name="modifierVk"/> across a tap of the target key.</summary>
    public static void Chord(ushort modifierVk, ushort virtualKey, ushort scanCode, int taps = 1)
    {
        KeyDown(modifierVk, 0);
        Thread.Sleep(30);

        for (var i = 0; i < taps; i++)
        {
            Press(virtualKey, scanCode);
            Thread.Sleep(60);
        }

        KeyUp(modifierVk, 0);
        Thread.Sleep(30);
    }

    private static void Send(ushort vk, ushort scan, bool keyUp)
    {
        var input = new INPUT[1];
        input[0] = new INPUT
        {
            type = Hooks.INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = keyUp ? Hooks.KEYEVENTF_KEYUP : 0,
                    dwExtraInfo = TestExtraInfo,
                },
            },
        };

        Hooks.SendInput(1, input, Marshal.SizeOf<INPUT>());
    }
}
