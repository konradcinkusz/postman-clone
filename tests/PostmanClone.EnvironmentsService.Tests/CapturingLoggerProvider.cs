using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PostmanClone.EnvironmentsService.Tests;

/// <summary>Keeps every formatted log line, and every exception, so a test can search the whole output.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    /// <summary>All log output so far, one entry per call, exceptions included.</summary>
    public IReadOnlyCollection<string> Lines => _lines.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_lines);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lines.Enqueue(formatter(state, exception));
            if (exception is not null)
            {
                lines.Enqueue(exception.ToString());
            }
        }
    }
}
