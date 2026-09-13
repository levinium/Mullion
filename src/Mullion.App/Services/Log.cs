using System.Diagnostics;
using System.Text;

namespace Mullion.App.Services;

/// <summary>
/// A small rolling file log.
/// <para>
/// A tray app fails while nobody is watching it, so "it stopped working
/// yesterday" needs something to look at. Deliberately hand-rolled rather than
/// pulling in a logging framework: the requirements here are one file, a size
/// cap, and never blocking the caller.
/// </para>
/// <para>
/// It records what Mullion DID - hook reinstalls, move outcomes, config
/// problems - and never what was typed. Only the names of chords that matched a
/// binding are written. A global keyboard hook attracts enough scrutiny without
/// also keeping a record of keystrokes, and that is far easier to honor from
/// the start than to retrofit.
/// </para>
/// </summary>
public sealed class Log : IDisposable
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private const int KeepFiles = 5;

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _path;
    private StreamWriter? _writer;

    public Log(string? directory = null)
    {
        _directory = directory ?? DefaultDirectory;
        _path = Path.Combine(_directory, "mullion.log");

        try
        {
            Directory.CreateDirectory(_directory);
            RollIfNeeded();
            _writer = new StreamWriter(_path, append: true, Encoding.UTF8) { AutoFlush = true };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not being able to log must never stop the app running.
            _writer = null;
        }
    }

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Mullion", "logs");

    public string Path_ => _path;

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? e = null) =>
        Write("ERROR", e is null ? message : $"{message} :: {e.GetType().Name}: {e.Message}");

    private void Write(string level, string message)
    {
        Debug.WriteLine($"[{level}] {message}");

        lock (_gate)
        {
            if (_writer is null) return;

            try
            {
                _writer.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {level,-5} {message}");
            }
            catch (IOException)
            {
                _writer = null;
            }
        }
    }

    /// <summary>Rotate on size rather than on date: a bad day produces far more than a busy one.</summary>
    private void RollIfNeeded()
    {
        var file = new FileInfo(_path);
        if (!file.Exists || file.Length < MaxBytes) return;

        for (var i = KeepFiles - 1; i >= 1; i--)
        {
            var from = Path.Combine(_directory, $"mullion.{i}.log");
            var to = Path.Combine(_directory, $"mullion.{i + 1}.log");
            if (File.Exists(from)) File.Move(from, to, overwrite: true);
        }

        File.Move(_path, Path.Combine(_directory, "mullion.1.log"), overwrite: true);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
