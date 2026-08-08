using System.IO;
using System.Text;
using System.Text.Json;

namespace CEI.ReportGenerator.App;

public sealed class ApplicationSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public ApplicationSettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CEI Report Generator",
            "settings.json");
    }

    public string FilePath { get; }

    public string? LastLoadError { get; private set; }

    public ApplicationSettings Load()
    {
        LastLoadError = null;

        if (!File.Exists(FilePath))
        {
            return ApplicationSettings.CreateDefaults();
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<ApplicationSettings>(json, Options) ?? ApplicationSettings.CreateDefaults();
            settings.RecentProjectLimit = ApplicationSettingsValidator.NormalizeRecentProjectLimit(settings.RecentProjectLimit);
            if (string.IsNullOrWhiteSpace(settings.DefaultProjectsFolder))
            {
                settings.DefaultProjectsFolder = ApplicationSettings.CreateDefaults().DefaultProjectsFolder;
            }

            return settings;
        }
        catch (Exception ex)
        {
            LastLoadError = ex.Message;
            return ApplicationSettings.CreateDefaults();
        }
    }

    public void Save(ApplicationSettings settings)
    {
        var errors = ApplicationSettingsValidator.Validate(settings);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(errors[0]);
        }

        Directory.CreateDirectory(settings.DefaultProjectsFolder);

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(directory ?? Directory.GetCurrentDirectory(), $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(settings, Options);
        File.WriteAllText(tempPath, json, Encoding.UTF8);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    public ApplicationSettings ResetToDefaults()
        => ApplicationSettings.CreateDefaults();
}
