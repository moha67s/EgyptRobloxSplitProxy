using System;
using System.IO;

namespace EgyptRobloxSplitProxy.Logging;

public static class Logger
{
    private static readonly object _lock = new();
    private static readonly string _logDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EgyptRobloxSplitProxy", "logs");
    private static readonly string _logFile = Path.Combine(_logDir, "app.log");

    static Logger()
    {
        try { Directory.CreateDirectory(_logDir); } catch { /* ignore */ }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);
    public static void Error(Exception ex) => Write("ERROR", ex.ToString());

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        try
        {
            lock (_lock)
            {
                File.AppendAllText(_logFile, line + Environment.NewLine);
            }
        }
        catch { /* never throw from logger */ }
        System.Diagnostics.Debug.WriteLine(line);
    }

    public static string LogFilePath => _logFile;
}