using System.Diagnostics;
using BatteryHub.Core.Logging;
using Microsoft.Extensions.Logging;

namespace BatteryHub.App;

/// <summary>The app's one log file (%LocalAppData%\BatteryHub\logs\batteryhub.log, rolled at 1 MB, 3 old files kept).</summary>
internal static class AppLog
{
    public static RollingFileLog File { get; } = new(RollingFileLog.DefaultDirectory);
}

/// <summary>Writes to the app's log file and to the debugger output.</summary>
internal sealed class AppLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        string message = formatter(state, exception);
        AppLog.File.Write(logLevel, typeof(T).Name, message, exception);
        Trace.WriteLine($"{DateTime.Now:HH:mm:ss} {logLevel} {typeof(T).Name}: {message}{(exception is null ? "" : Environment.NewLine + exception)}");
    }
}
