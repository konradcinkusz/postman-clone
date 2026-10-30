using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PostmanClone.ExecutionService.Tests.Support;

/// <summary>Keeps every formatted log message the service writes, so a test can prove a secret never reached a log.</summary>
public sealed class CapturingLogs : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingLogs owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            owner.Messages.Enqueue(exception is null ? text : $"{text} {exception}");
        }
    }
}
