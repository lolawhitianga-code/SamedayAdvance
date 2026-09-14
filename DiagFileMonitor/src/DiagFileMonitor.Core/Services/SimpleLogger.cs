namespace DiagFileMonitor.Core.Services;

/// <summary>Minimal best-effort file logger for the folder-watching pipeline (bad zips, missing machine.xml, etc).</summary>
public static class SimpleLogger
{
    private static readonly object Lock = new();
    private static string? _logFilePath;

    public static void Initialize(string logFilePath)
    {
        _logFilePath = logFilePath;
        var dir = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} :: {ex}");

    private static void Write(string level, string message)
    {
        if (_logFilePath is null) return;

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
        lock (Lock)
        {
            try
            {
                File.AppendAllText(_logFilePath, line);
            }
            catch
            {
                // Logging is best-effort; never let it break the pipeline.
            }
        }
    }
}
