using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Zentory.Tests.Support;

// Proveedor de logs de prueba (PLAN-0001, CA-33): guarda en memoria cada entrada que el host escribe para que el test
// pueda afirmar sobre nivel y mensaje. Escrito a mano para no sumar librerías al proyecto de tests (RF-10).
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

    public IReadOnlyCollection<CapturedLogEntry> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose() { }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            entries.Enqueue(new CapturedLogEntry(category, logLevel, formatter(state, exception)));
        }
    }
}

public sealed record CapturedLogEntry(string Category, LogLevel Level, string Message);
