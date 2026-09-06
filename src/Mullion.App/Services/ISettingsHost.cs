using Mullion.App.ViewModels;
using Mullion.Core.Abstractions;
using Mullion.Core.Hotkeys;

namespace Mullion.App.Services;

/// <param name="Key">Display label, e.g. "Win+A".</param>
/// <param name="Zone">What that key moves the window to.</param>
/// <param name="Row">Grid position, so a rebind knows what it is moving.</param>
public sealed record BindingEntry(string Key, string Zone, int Row, int Col);

public sealed record RebindResult(bool Success, string Message);

public sealed record SettingsSnapshot(
    AutoStartMode AutoStart,
    bool CanConfigureElevatedAutoStart,
    bool IsElevated,
    bool ShowZoneFlash,
    bool AllowSpanningUnions,
    string WinKeySuppression,
    string SurfaceId,
    IReadOnlyList<(string Id, string Name)> AvailableSurfaces,
    IReadOnlyList<BindingEntry> Bindings,
    string ConfigPath);

public interface ISettingsHost
{
    SettingsSnapshot GetSettings();

    /// <summary>Returns an error message when the change could not be applied.</summary>
    string? SetAutoStart(AutoStartMode mode);

    void SetShowZoneFlash(bool value);

    void SetAllowSpanningUnions(bool value);

    void SetWinKeySuppression(string value);

    void SetKeySurface(string surfaceId);

    void RestartElevated();

    void OpenConfigFolder();

    void RerunWizard();

    /// <summary>
    /// Listen for the next chord and move the zone at <paramref name="row"/>,
    /// <paramref name="col"/> onto that key.
    /// <para>
    /// Capture runs through the keyboard hook because the UI framework never
    /// sees Win-modified keys, so this cannot be a plain KeyDown handler.
    /// </para>
    /// </summary>
    void BeginRebind(int row, int col, Action<RebindResult> completed);

    void CancelRebind();

    /// <summary>Discard customizations and regenerate from the current displays.</summary>
    void ResetLayout();

    /// <summary>
    /// The monitor diagram, wired so clicking a zone starts a rebind. Built by
    /// the host because only it holds the current displays and layout.
    /// </summary>
    MonitorDiagramViewModel BuildInteractiveDiagram(Action<GridPos> onZoneActivated);
}
