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
