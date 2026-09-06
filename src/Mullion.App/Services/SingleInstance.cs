namespace Mullion.App.Services;

/// <summary>
/// Ensures only one Mullion runs per user session.
/// <para>
/// This is not tidiness. Two instances would each install a keyboard hook and
/// both would act on the same keypress, so a single Win+A would move the window
/// twice - and the second move would land on whatever the first one produced.
/// </para>
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex? _mutex;

    public SingleInstance(string name = "Mullion")
    {
        // Local\ rather than Global\: per-session is the right scope, so fast
        // user switching gives each logged-in user their own instance.
        _mutex = new Mutex(initiallyOwned: true, $"Local\\{name}.SingleInstance", out var created);
        IsPrimary = created;

        if (!created)
        {
            _mutex.Dispose();
            _mutex = null;
        }
    }

    public bool IsPrimary { get; }

    public void Dispose()
    {
        if (_mutex is null) return;

        try { _mutex.ReleaseMutex(); }
        catch (ApplicationException) { /* not owned; nothing to release */ }

        _mutex.Dispose();
    }
}
