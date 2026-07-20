using System.IO;

namespace PowerMonitor.Services;

/// <summary>Tiny append-only file logger; never throws.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string LogDir => Path.Combine(Models.AppSettings.Dir, "logs");

    public static void Info(string msg) => Write("INFO ", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    public static void Error(string context, Exception ex) =>
        Write("ERROR", $"{context}: {ex}"); // ToString includes type, message, inner + stack

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
