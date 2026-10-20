using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace BatteryHub.Core.Logging;

/// <summary>
/// Writes log lines to <c>batteryhub.log</c> in one folder. When the file would pass <see cref="MaxBytes"/> it becomes
/// <c>batteryhub.1.log</c> (older files move up one number) and a new file starts; only <see cref="KeepFiles"/> old
/// files are kept. Never throws: when the folder cannot be written, lines are dropped until a later write succeeds.
/// </summary>
public sealed class RollingFileLog : IDisposable
{
    public const string FileName = "batteryhub.log";

    // A failed open is retried at most this often, so a read-only folder does not cost a file open per line.
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);

    private readonly Lock _lock = new();
    private readonly TimeProvider _time;
    private FileStream? _stream; // under _lock
    private DateTimeOffset _retryAt = DateTimeOffset.MinValue; // under _lock
    private bool _disposed; // under _lock

    public RollingFileLog(string directory, long maxBytes = 1024 * 1024, int keepFiles = 3, TimeProvider? time = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1024);
        ArgumentOutOfRangeException.ThrowIfNegative(keepFiles);
        Directory = directory;
        MaxBytes = maxBytes;
        KeepFiles = keepFiles;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>%LocalAppData%\BatteryHub\logs: logs belong to this machine, so they do not roam with the profile.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatteryHub", "logs");

    public string Directory { get; }

    public long MaxBytes { get; }

    public int KeepFiles { get; }

    public string FilePath => Path.Combine(Directory, FileName);

    public LogLevel MinimumLevel { get; init; } = LogLevel.Information;

    public ILogger<T> CreateLogger<T>() => new Logger<T>(this);

    /// <summary>Appends one entry: time, level, category, message and the exception (with its stack) if any.</summary>
    public void Write(LogLevel level, string category, string message, Exception? exception)
    {
        if (level < MinimumLevel || level == LogLevel.None)
        {
            return;
        }

        var now = _time.GetLocalNow();
        var text = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(' ').Append(Abbreviation(level))
            .Append(' ').Append(category)
            .Append(": ").Append(message);
        if (exception is not null)
        {
            text.AppendLine().Append(exception);
        }

        byte[] line = Encoding.UTF8.GetBytes(text.AppendLine().ToString());
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                if (_stream is not null && _stream.Length > 0 && _stream.Length + line.Length > MaxBytes)
                {
                    Roll();
                }

                if (_stream is null && !TryOpen(now))
                {
                    return;
                }

                _stream!.Write(line);
                _stream.Flush();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Disk full, file deleted under us, ...: drop this line and start again on a later one.
                CloseQuietly();
                _retryAt = now + RetryAfterFailure;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            CloseQuietly();
        }
    }

    private static string Abbreviation(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        _ => "CRT",
    };

    private bool TryOpen(DateTimeOffset now)
    {
        if (now < _retryAt)
        {
            return false;
        }

        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            // Others may read (or delete) the log while the app runs.
            _stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _retryAt = now + RetryAfterFailure;
            return false;
        }
    }

    // batteryhub.log -> batteryhub.1.log -> ... -> batteryhub.<KeepFiles>.log, the oldest dropped.
    private void Roll()
    {
        CloseQuietly();
        string Numbered(int n) => Path.Combine(Directory, $"batteryhub.{n}.log");

        if (KeepFiles == 0)
        {
            File.Delete(FilePath);
            return;
        }

        File.Delete(Numbered(KeepFiles));
        for (int n = KeepFiles - 1; n >= 1; n--)
        {
            if (File.Exists(Numbered(n)))
            {
                File.Move(Numbered(n), Numbered(n + 1));
            }
        }

        File.Move(FilePath, Numbered(1));
    }

    private void CloseQuietly()
    {
        try
        {
            _stream?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        _stream = null;
    }

    private sealed class Logger<T>(RollingFileLog log) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= log.MinimumLevel && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                log.Write(logLevel, typeof(T).Name, formatter(state, exception), exception);
            }
        }
    }
}
