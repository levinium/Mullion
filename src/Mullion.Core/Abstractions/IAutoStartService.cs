namespace Mullion.Core.Abstractions;

public enum AutoStartMode
{
    /// <summary>Not registered to start with the session.</summary>
    Disabled,

    /// <summary>HKCU Run entry. Starts at medium integrity, no UAC prompt.</summary>
    Standard,

    /// <summary>
    /// Scheduled task with highest privileges. Starts elevated at logon with no
    /// UAC prompt, which is the only way to manage elevated windows without
    /// consenting on every launch.
    /// </summary>
    Elevated,
}

public sealed record AutoStartStatus(
    AutoStartMode Mode,
    bool CanConfigureElevated,
    string? Detail = null);

public interface IAutoStartService
{
    AutoStartStatus GetStatus();

    /// <summary>
    /// Apply the requested mode. Configuring <see cref="AutoStartMode.Elevated"/>
    /// itself requires elevation, so this returns false rather than throwing when
    /// the current process cannot do it.
    /// </summary>
    bool TrySetMode(AutoStartMode mode, out string? error);
}
