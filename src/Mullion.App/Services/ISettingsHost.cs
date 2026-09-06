using Mullion.Core.Abstractions;

namespace Mullion.App.Services;

public sealed record SettingsSnapshot(
    AutoStartMode AutoStart,
    bool CanConfigureElevatedAutoStart,
    bool IsElevated,
    bool ShowZoneFlash,
    bool AllowSpanningUnions,
    string WinKeySuppression,
    string SurfaceId,
    IReadOnlyList<(string Id, string Name)> AvailableSurfaces,
    IReadOnlyList<(string Key, string Zone)> Bindings,
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
}
