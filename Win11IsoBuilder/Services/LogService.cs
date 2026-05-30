using System.IO;

namespace Win11IsoBuilder.Services;

public enum LogLevel { Trace, Info, Warn, Error }

public sealed record LogEntry(DateTime Timestamp, LogLevel Level, string Message)
{
    public override string ToString() => $"{Timestamp:HH:mm:ss} [{Level,-5}] {Message}";
}

/// <summary>Abstraction every service logs through (keeps services UI-agnostic).</summary>
public interface ILogSink
{
    void Trace(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

/// <summary>
/// Central log: appends to a per-session file under %TEMP%\Win11IsoBuilder and raises
/// an event so the UI can mirror entries live. Thread-safe (file writes are locked).
/// </summary>
public sealed class LogService : ILogSink
{
    private readonly object _gate = new();
    private readonly string _logFilePath;

    public event EventHandler<LogEntry>? EntryLogged;
    public string LogFilePath => _logFilePath;

    public LogService()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Win11IsoBuilder");
        Directory.CreateDirectory(dir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        _logFilePath = Path.Combine(dir, $"build-{stamp}.log");
    }

    public void Trace(string message) => Write(LogLevel.Trace, message);
    public void Info(string message) => Write(LogLevel.Info, message);
    public void Warn(string message) => Write(LogLevel.Warn, message);
    public void Error(string message) => Write(LogLevel.Error, message);

    private void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message);
        lock (_gate)
        {
            try { File.AppendAllText(_logFilePath, entry + Environment.NewLine); }
            catch { /* logging must never throw into the pipeline */ }
        }
        EntryLogged?.Invoke(this, entry);
    }
}
