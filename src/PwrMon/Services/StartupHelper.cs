using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace PwrMon.Services;

/// <summary>
/// "Start with Windows" wiring. Three mechanisms exist, though this class only ever writes
/// two of them:
///  - HKCU Run key (normal start), written here,
///  - a Task Scheduler entry with RunLevel=Highest (elevated start without a UAC prompt
///    each logon — the same pattern G-Helper uses), written here. Creating/removing the
///    task requires the current process to be elevated.
///  - a machine-wide HKLM Run key, which installers before v1.6.2 wrote from their own
///    "start with Windows" checkbox. That checkbox is gone — autostart has one owner now,
///    this class — and Setup deletes the value on install. This class never writes it, but
///    still reads it (<see cref="IsMachineRunKeyEnabled"/>) so the Settings checkbox tells
///    the truth on a machine that still carries one, and clears it in <see cref="Disable"/>
///    when running elevated.
/// </summary>
public static class StartupHelper
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "PwrMon";
    private const string TaskName = "PwrMon Autostart";

    public static string ExePath => Environment.ProcessPath ?? "";

    /// <summary>Rights that would let a principal swap the executable out for another one —
    /// either by rewriting the file or by deleting and recreating it in the folder.
    /// (On a directory, WriteData == CreateFiles and AppendData == CreateDirectories.)</summary>
    private const FileSystemRights ReplaceRights =
        FileSystemRights.WriteData |
        FileSystemRights.AppendData |
        FileSystemRights.Delete |
        FileSystemRights.DeleteSubdirectoriesAndFiles |
        FileSystemRights.ChangePermissions |
        FileSystemRights.TakeOwnership;

    /// <summary>
    /// True when a non-elevated process could replace the executable, or drop a replacement
    /// beside it. An elevated logon task pointing at such a path is a privilege-escalation
    /// path: anything running as the user swaps the binary and inherits admin at next logon.
    /// Portable builds living in Downloads/Desktop/%LocalAppData% are exactly this case;
    /// an installed build under Program Files is not.
    /// Fails closed — an ACL we can't read counts as writable.
    /// </summary>
    public static bool IsExePathUserWritable() => IsReplaceableByNonAdmins(ExePath);

    /// <summary>The path-taking core of <see cref="IsExePathUserWritable"/>, split out so the
    /// predicate can be tested against known-protected and known-writable locations.</summary>
    internal static bool IsReplaceableByNonAdmins(string path)
    {
        if (string.IsNullOrEmpty(path)) return true;
        try
        {
            // Rewriting the file itself and replacing it via its parent directory are both
            // enough, so either one granting write means the path can't be trusted.
            if (File.Exists(path) &&
                GrantsReplaceToNonAdmins(new FileInfo(path).GetAccessControl(AccessControlSections.Access)))
                return true;

            var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return true;
            return GrantsReplaceToNonAdmins(
                new DirectoryInfo(dir).GetAccessControl(AccessControlSections.Access));
        }
        catch (Exception ex)
        {
            Log.Error("exe path ACL check", ex);
            return true;
        }
    }

    private static bool GrantsReplaceToNonAdmins(FileSystemSecurity sec)
    {
        foreach (FileSystemAccessRule rule in
                 sec.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            if ((rule.FileSystemRights & ReplaceRights) == 0) continue;
            if (rule.IdentityReference is SecurityIdentifier sid && !IsPrivileged(sid)) return true;
        }
        return false;
    }

    /// <summary>Principals that already hold admin-equivalent power, so granting them write
    /// access to the exe doesn't widen anything. CREATOR OWNER only ever applies to objects
    /// the principal was already allowed to create.</summary>
    private static bool IsPrivileged(SecurityIdentifier sid) =>
        sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) ||
        sid.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
        sid.IsWellKnown(WellKnownSidType.CreatorOwnerSid) ||
        sid.Value.StartsWith("S-1-5-80-", StringComparison.Ordinal); // service SIDs, incl. TrustedInstaller

    public static bool IsRunKeyEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(AppName) is string;
    }

    /// <summary>The installer's machine-wide autostart entry (installer/PwrMon.iss), which
    /// this class doesn't create — only HKLM is writable there, and Setup already runs
    /// elevated when it writes it. Reading HKLM needs no special rights.</summary>
    public static bool IsMachineRunKeyEnabled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(RunKeyPath);
        return key?.GetValue(AppName) is string;
    }

    public static bool IsElevatedTaskEnabled()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks", $"/Query /TN \"{TaskName}\"")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi);
            p!.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>Clears every autostart mechanism: this app's own HKCU Run key and scheduled
    /// task, plus the machine-wide HKLM entry written by installers before v1.6.2. The last
    /// one needs admin rights, so it succeeds only when PwrMon is running elevated — callers
    /// read the state back (<see cref="IsMachineRunKeyEnabled"/>) rather than assuming.</summary>
    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch (Exception ex) { Log.Error("run key remove", ex); }
        RunSchtasks($"/Delete /TN \"{TaskName}\" /F");

        try
        {
            using var machineKey = Registry.LocalMachine.OpenSubKey(RunKeyPath, writable: true);
            machineKey?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch (Exception ex) { Log.Info($"machine run key not removed (needs admin): {ex.Message}"); }
    }

    /// <summary>Enable startup; elevated=true creates the scheduled task (requires admin now).</summary>
    public static bool Enable(bool elevated)
    {
        // Guard before Disable() so a refusal leaves any existing autostart intact.
        if (elevated && IsExePathUserWritable())
        {
            Log.Error($"refusing elevated autostart: '{ExePath}' can be replaced by a non-admin process");
            return false;
        }

        Disable(); // clear both mechanisms first so exactly one is active
        if (elevated)
        {
            var ok = RunSchtasks($"/Create /TN \"{TaskName}\" /TR \"\\\"{ExePath}\\\" --minimized\" /SC ONLOGON /RL HIGHEST /F");
            if (!ok) Log.Error("schtasks create failed (not elevated?)");
            return ok;
        }
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key.SetValue(AppName, $"\"{ExePath}\" --minimized");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("run key set", ex);
            return false;
        }
    }

    private static bool RunSchtasks(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks", args)
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi);
            p!.WaitForExit(10000);
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Error("schtasks", ex);
            return false;
        }
    }

    /// <summary>Relaunch the app elevated; returns false if the user declined UAC.</summary>
    public static bool RestartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ExePath, "--replace")
            {
                Verb = "runas",
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false; // UAC declined
        }
    }
}
