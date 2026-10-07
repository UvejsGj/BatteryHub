using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatteryHub.Core.Settings;

/// <summary>Loads and saves <see cref="AppSettings"/> as settings.json in one folder.</summary>
public sealed class SettingsStore(string directory, ILogger<SettingsStore>? logger = null)
{
    public const string FileName = "settings.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;

    /// <summary>%AppData%\BatteryHub.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BatteryHub");

    public string FilePath { get; } = Path.Combine(directory, FileName);

    /// <summary>
    /// Returns the saved settings, or defaults if there is no file. Never throws: the app must start even when its
    /// settings cannot be read. A file that cannot be parsed is renamed to settings.json.bad (so a hand edit is not
    /// lost); a file that cannot be read at all (permissions, offline network profile) is left alone.
    /// </summary>
    public AppSettings Load()
    {
        string text;
        try
        {
            text = File.ReadAllText(FilePath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read {Path}; using defaults", FilePath);
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(text, Json) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            string bad = FilePath + ".bad";
            _logger.LogWarning(ex, "{Path} is not valid settings JSON; moving it to {Bad} and using defaults", FilePath, bad);
            try
            {
                File.Move(FilePath, bad, overwrite: true);
            }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(moveError, "Could not move {Path} aside", FilePath);
            }

            return new AppSettings();
        }
    }

    /// <summary>
    /// Writes a temporary file, flushes it to disk, then renames it over settings.json, so neither a crash nor a power
    /// cut leaves a half-written settings file.
    /// </summary>
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temp = FilePath + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, settings, Json);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, FilePath, overwrite: true);
    }
}
