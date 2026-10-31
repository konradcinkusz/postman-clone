using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PostmanClone.Web.Tests.Collections;

/// <summary>
/// Keeps every formatted log line the hosted Web writes, so a test can check what was not logged (WP23: no file contents in a
/// log). The lines are the formatted messages and their exceptions, which is what an operator would read.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    /// <summary>Every line written so far, oldest first.</summary>
    public IReadOnlyCollection<string> Lines => _lines;

    public ILogger CreateLogger(string categoryName) => new Sink(_lines);

    public void Dispose()
    {
    }

    private sealed class Sink(ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            lines.Enqueue(exception is null ? text : $"{text} {exception}");
        }
    }
}
