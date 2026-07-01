using System.Diagnostics;
using Microsoft.Win32;

namespace PowerMonitor.Services;

/// <summary>
/// "Start with Windows" wiring. Two mechanisms:
///  - HKCU Run key (normal start),
///  - a Task Scheduler entry with RunLevel=Highest (elevated start without a UAC prompt
///    each logon — the same pattern G-Helper uses). Creating/removing the task requires
///    the current process to be elevated.
/// </summary>
public static class StartupHelper
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "PowerMonitor";
    private const string TaskName = "PowerMonitor Autostart";

    public static string ExePath => Environment.ProcessPath ?? "";

    public static bool IsRunKeyEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
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

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch (Exception ex) { Log.Error("run key remove", ex); }
        RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
    }

    /// <summary>Enable startup; elevated=true creates the scheduled task (requires admin now).</summary>
    public static bool Enable(bool elevated)
    {
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
