using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BatteryHub.App;

/// <summary>Writes log lines to the debugger output. A log file comes with milestone 6.</summary>
internal sealed class TraceLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
        {
            Trace.WriteLine($"{DateTime.Now:HH:mm:ss} {logLevel} {typeof(T).Name}: {formatter(state, exception)}{(exception is null ? "" : Environment.NewLine + exception)}");
        }
    }
}
