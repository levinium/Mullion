using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mullion.Platform.Windows.Native;

namespace Mullion.Platform.Windows.Displays;

/// <summary>
/// Raises a single, debounced event when the display arrangement changes.
/// <para>
/// Uses a hidden TOP-LEVEL window, deliberately not a message-only one.
/// WM_DISPLAYCHANGE and WM_SETTINGCHANGE are broadcast messages, and Windows
/// does not deliver broadcasts to HWND_MESSAGE windows - a message-only window
/// here compiles, runs, and silently never fires. Several distinct messages
/// describe the same change, so they are coalesced.
/// </para>
/// <para>
/// The debounce is not optional. A dock or undock emits three to five
/// intermediate topologies as Windows brings displays up one at a time, and
/// acting on each produces visible thrash and a pile of near-duplicate profiles.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DisplayChangeWatcher : IDisposable
{
    private const uint SPI_SETWORKAREA = 0x002F;

    private readonly TimeSpan _debounce;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);

    private WindowClass.WndProc? _proc;   // held so the GC cannot collect it
    private Timer? _timer;
    private nint _hwnd;
    private uint _threadId;

    public DisplayChangeWatcher(TimeSpan? debounce = null)
    {
        _debounce = debounce ?? TimeSpan.FromMilliseconds(750);

        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Mullion.DisplayWatcher",
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>Raised on a threadpool thread once the arrangement has settled.</summary>
    public event Action? Changed;

    /// <summary>Every relevant message as it arrives, before debouncing. Diagnostics only.</summary>
    public event Action<string>? MessageObserved;

    /// <summary>The watcher window handle, so a caller can confirm it was created.</summary>
    public nint Handle => _hwnd;

    private void Run()
    {
        _threadId = WindowClass.GetCurrentThreadId();
        _proc = WndProc;

        var className = $"MullionDisplayWatcher_{Guid.NewGuid():N}";
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

            if (WindowClass.RegisterClassEx(ref wc) == 0) { _ready.Set(); return; }

            // Top-level (parent 0) and never shown: WS_EX_TOOLWINDOW keeps it
            // out of the taskbar and Alt+Tab, and without WS_VISIBLE it never
            // paints. Passing HWND_MESSAGE here would cost us every broadcast.
            _hwnd = WindowClass.CreateWindowEx(
                (uint)WindowClass.WS_EX_TOOLWINDOW, className, "Mullion display watcher",
                0, 0, 0, 0, 0,
                0, 0, wc.hInstance, 0);

            _ready.Set();

            while (WindowClass.GetMessage(out var msg, 0, 0, 0) > 0)
            {
                WindowClass.TranslateMessage(ref msg);
                WindowClass.DispatchMessage(ref msg);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(classNamePtr);
        }
    }

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        var relevant = msg switch
        {
            WindowClass.WM_DISPLAYCHANGE => true,
            WindowClass.WM_DEVICECHANGE => true,
            WindowClass.WM_DPICHANGED => true,

            // Fires for many settings; only the work-area one matters here,
            // and it is how a taskbar move or auto-hide toggle reaches us.
            WindowClass.WM_SETTINGCHANGE => (uint)wParam == SPI_SETWORKAREA,

            _ => false,
        };

        if (msg is WindowClass.WM_DISPLAYCHANGE or WindowClass.WM_DEVICECHANGE
                or WindowClass.WM_DPICHANGED or WindowClass.WM_SETTINGCHANGE)
        {
            MessageObserved?.Invoke($"msg=0x{msg:X4} wParam=0x{wParam:X} relevant={relevant}");
        }

        if (relevant) Debounce();

        return WindowClass.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void Debounce()
    {
        // Restarting the timer on every message is what collapses a burst of
        // intermediate topologies into one notification.
        _timer?.Dispose();
        _timer = new Timer(_ => Changed?.Invoke(), null, _debounce, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;

        if (_hwnd != 0)
        {
            WindowClass.DestroyWindow(_hwnd);
            _hwnd = 0;
        }

        if (_threadId != 0) WindowClass.PostThreadMessage(_threadId, WindowClass.WM_QUIT, 0, 0);

        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
        GC.KeepAlive(_proc);
    }
}
