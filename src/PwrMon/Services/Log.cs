using System.IO;

namespace PwrMon.Services;

/// <summary>Tiny append-only file logger; never throws.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string LogDir => Path.Combine(Models.AppSettings.Dir, "logs");

    public static void Info(string msg) => Write("INFO ", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    public static void Error(string context, Exception ex) =>
        Write("ERROR", $"{context}: {ex}"); // ToString includes type, message, inner + stack

    /// <summary>
    /// Deletes daily logs older than the retention window. Without this the log directory
    /// only ever grows — history CSVs are pruned, so logs outliving them was an oversight.
    /// </summary>
    public static void CleanupOldFiles(int retentionDays)
    {
        try
        {
            if (!Directory.Exists(LogDir)) return;
            var cutoff = DateTime.Now.Date.AddDays(-retentionDays);
            foreach (var file in Directory.GetFiles(LogDir, "app-????????.log"))
            {
                var name = Path.GetFileNameWithoutExtension(file); // app-yyyyMMdd
                if (DateTime.TryParseExact(name[4..], "yyyyMMdd", null,
                        System.Globalization.DateTimeStyles.None, out var day) && day < cutoff)
                    File.Delete(file);
            }
        }
        catch
        {
            // same policy as Write: logging housekeeping must never take the app down
        }
    }

    private static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDir);
                var file = Path.Combine(LogDir, $"app-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss.fff} {level} {msg}{Environment.NewLine}");
            }
        }
        catch
        {
            // logging must never take the app down
        }
    }
}
