using Blazor_Bedrock.Data.Models;

namespace Blazor_Bedrock.Services.Logger;

public interface IApplicationLoggerService
{
    event Action? OnLogAdded;
    List<LogEntry> GetLogs();
    void Log(LogLevel level, string message, string? category = null, Exception? exception = null);
    void Clear();
}

public class ApplicationLoggerService : IApplicationLoggerService
{
    private readonly List<LogEntry> _logs = new();
    private readonly object _lock = new();
    private const int MaxLogs = 1000;

    public event Action? OnLogAdded;

    public List<LogEntry> GetLogs()
    {
        lock (_lock)
        {
            return new List<LogEntry>(_logs);
        }
    }

    public void Log(LogLevel level, string message, string? category = null, Exception? exception = null)
    {
        lock (_lock)
        {
            var entry = new LogEntry
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Level = level,
                Message = message,
                Category = category ?? "Application",
                Exception = exception?.ToString()
            };

            _logs.Add(entry);

            // Keep only the last MaxLogs entries
            if (_logs.Count > MaxLogs)
            {
                _logs.RemoveAt(0);
            }

            OnLogAdded?.Invoke();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _logs.Clear();
            OnLogAdded?.Invoke();
        }
    }
}

public class LogEntry
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Exception { get; set; }
}

