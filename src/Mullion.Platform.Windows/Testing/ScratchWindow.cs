using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mullion.Platform.Windows.Native;

namespace Mullion.Platform.Windows.Testing;

/// <summary>
/// A real, resizable top-level window with its own message pump, used to
/// exercise the window mover end to end.
/// <para>
/// Creating our own beats driving a shipped app: Windows 11 Notepad is a
/// packaged app whose MainWindowHandle never resolves, and anything else we
/// might launch is both unpredictable and rude to whatever the user has open.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ScratchWindow : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private WindowClass.WndProc? _proc;   // held so the GC cannot collect the delegate
    private uint _threadId;

    public nint Handle { get; private set; }

    public ScratchWindow(string title = "Mullion scratch window")
    {
        _thread = new Thread(() => Run(title))
        {
            IsBackground = true,
            Name = "Mullion.ScratchWindow",
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Scratch window did not become ready.");
    }

    private void Run(string title)
    {
        _threadId = WindowClass.GetCurrentThreadId();
        _proc = WindowClass.DefWindowProc;

        var className = $"MullionScratch_{Guid.NewGuid():N}";
        var classNamePtr = Marshal.StringToHGlobalUni(className);

        try
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
                hInstance = WindowClass.GetModuleHandle(null),
                lpszClassName = classNamePtr,
            };

            if (WindowClass.RegisterClassEx(ref wc) == 0)
                throw new InvalidOperationException(
                    $"RegisterClassEx failed ({Marshal.GetLastWin32Error()}).");

            Handle = WindowClass.CreateWindowEx(
                0, className, title,
                (uint)(WindowClass.WS_OVERLAPPEDWINDOW | WindowClass.WS_VISIBLE),
                WindowClass.CW_USEDEFAULT, WindowClass.CW_USEDEFAULT, 900, 600,
                0, 0, wc.hInstance, 0);

            if (Handle == 0)
                throw new InvalidOperationException(
                    $"CreateWindowEx failed ({Marshal.GetLastWin32Error()}).");

            _ready.Set();

            while (WindowClass.GetMessage(out var msg, 0, 0, 0) > 0)
            {
                WindowClass.TranslateMessage(ref msg);
                WindowClass.DispatchMessage(ref msg);
            }
        }
        catch
        {
            _ready.Set();
            throw;
        }
        finally
        {
            Marshal.FreeHGlobal(classNamePtr);
        }
    }

    public void Focus()
    {
        if (Handle != 0) WindowClass.SetForegroundWindow(Handle);
    }

    /// <summary>
    /// Move and size the window from the outside, standing in for a person
    /// dragging its edge.
    /// <para>
    /// It has to come from here rather than from the probe: this is the one
    /// call in the whole exercise that Mullion must NOT recognize as its own,
    /// and the only way to be sure of that is for it not to go through the
    /// window manager at all.
    /// </para>
    /// </summary>
    public void ResizeByHand(int x, int y, int width, int height)
    {
        if (Handle == 0) return;

        Win.SetWindowPos(Handle, 0, x, y, width, height, Win.SWP_NOZORDER | Win.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Slide the window without touching its size, standing in for a person
    /// dragging it by the title bar.
    /// <para>
    /// Separate from <see cref="ResizeByHand"/> because the difference is the
    /// whole point: a drag moves a window and chooses no new size for it, and a
    /// stand-in that changed the size by even a pixel would be testing the
    /// other case.
    /// </para>
    /// </summary>
    public void DragByHand(int dx, int dy)
    {
        if (Handle == 0 || !Win.GetWindowRect(Handle, out var rect)) return;

        Win.SetWindowPos(
            Handle, 0, rect.Left + dx, rect.Top + dy,
            rect.Right - rect.Left, rect.Bottom - rect.Top,
            Win.SWP_NOZORDER | Win.SWP_NOACTIVATE);
    }

    public void Dispose()
    {
        if (Handle != 0)
        {
            WindowClass.PostMessage(Handle, WindowClass.WM_DESTROY, 0, 0);
            Handle = 0;
        }

        if (_threadId != 0) WindowClass.PostThreadMessage(_threadId, WindowClass.WM_QUIT, 0, 0);

        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
        GC.KeepAlive(_proc);
    }
}
