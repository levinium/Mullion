using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;
using Mullion.Core.Abstractions;

namespace Mullion.Platform.Windows.Windows;

/// <summary>
/// Two auto-start paths, because elevation forces the choice.
/// <para>
/// A plain HKCU Run entry always starts at medium integrity, so hotkeys go dead
/// whenever an elevated window has focus and elevated windows cannot be moved.
/// Launching elevated from Run is not possible without a UAC prompt on every
/// single logon, which nobody will tolerate. A scheduled task registered with
/// HighestAvailable is the supported way to start elevated at logon silently -
/// it is what tools in this category use, and it is opt-in because running
/// elevated is a real security posture change, not a convenience toggle.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAutoStartService(string? executablePath = null) : IAutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Mullion";
    private const string TaskName = "Mullion Elevated Autostart";

    private readonly string _exe = executablePath ?? Environment.ProcessPath ?? string.Empty;

    public AutoStartStatus GetStatus()
    {
        if (TaskExists())
            return new AutoStartStatus(AutoStartMode.Elevated, Elevation.IsCurrentProcessElevated,
                "Starts elevated at logon via Task Scheduler.");

        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        if (key?.GetValue(ValueName) is string)
            return new AutoStartStatus(AutoStartMode.Standard, Elevation.IsCurrentProcessElevated,
                "Starts with Windows at normal privilege.");

        return new AutoStartStatus(AutoStartMode.Disabled, Elevation.IsCurrentProcessElevated);
    }

    public bool TrySetMode(AutoStartMode mode, out string? error)
    {
        error = null;

        if (string.IsNullOrEmpty(_exe))
        {
            error = "Could not determine the Mullion executable path.";
            return false;
        }

        try
        {
            switch (mode)
            {
                case AutoStartMode.Disabled:
                    RemoveRunEntry();
                    return TryRemoveTask(out error);

                case AutoStartMode.Standard:
                    if (!TryRemoveTask(out error)) return false;
                    WriteRunEntry();
                    return true;

                case AutoStartMode.Elevated:
                    if (!Elevation.IsCurrentProcessElevated)
                    {
                        error = "Registering elevated auto-start requires running Mullion as administrator once. " +
                                "Use \"Restart as administrator\", then enable this again.";
                        return false;
                    }

                    RemoveRunEntry();
                    return TryCreateTask(out error);

                default:
                    error = $"Unknown mode {mode}.";
                    return false;
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            error = e.Message;
            return false;
        }
    }

    private void WriteRunEntry()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key?.SetValue(ValueName, $"\"{_exe}\" --tray");
    }

    private static void RemoveRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is not null) key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static bool TaskExists() => RunSchTasks($"/Query /TN \"{TaskName}\"", out _) == 0;

    private bool TryCreateTask(out string? error)
    {
        // Registering from XML rather than the /Create shorthand, because only
        // XML can express RunLevel=HighestAvailable together with disabling the
        // battery and idle conditions that would otherwise stop a laptop
        // starting Mullion on power.
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";

        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starts Mullion elevated at logon so it can manage windows that run as administrator.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>false</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>"{_exe}"</Command>
                  <Arguments>--tray</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;

        var path = Path.Combine(Path.GetTempPath(), $"mullion-task-{Guid.NewGuid():N}.xml");

        try
        {
            File.WriteAllText(path, xml, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

            var exit = RunSchTasks($"/Create /TN \"{TaskName}\" /XML \"{path}\" /F", out var output);
            if (exit == 0) { error = null; return true; }

            error = $"schtasks failed ({exit}): {output}";
            return false;
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    private static bool TryRemoveTask(out string? error)
    {
        error = null;
        if (!TaskExists()) return true;

        var exit = RunSchTasks($"/Delete /TN \"{TaskName}\" /F", out var output);
        if (exit == 0) return true;

        error = $"Could not remove the scheduled task ({exit}): {output}";
        return false;
    }

    private static int RunSchTasks(string arguments, out string output)
    {
        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        if (process is null) { output = "Could not start schtasks.exe."; return -1; }

        output = (process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd()).Trim();
        process.WaitForExit(15000);
        return process.HasExited ? process.ExitCode : -1;
    }
}
