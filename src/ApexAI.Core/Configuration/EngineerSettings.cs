using System.Text.Json;

namespace ApexAI.Core.Configuration;

public enum EngineerProvider { Offline, OpenAiCompatible }

public sealed record EngineerSettings(
    EngineerProvider Provider = EngineerProvider.Offline,
    string Endpoint = "https://api.openai.com/v1/chat/completions",
    string Model = "gpt-4o-mini",
    int TelemetryPort = 9000);

public sealed class EngineerSettingsStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public EngineerSettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ApexAI", "settings.json");
    }

    public EngineerSettings Load()
    {
        if (!File.Exists(_path)) return new EngineerSettings();
        try { return JsonSerializer.Deserialize<EngineerSettings>(File.ReadAllText(_path), _options) ?? new EngineerSettings(); }
        catch (JsonException) { return new EngineerSettings(); }
    }

    public void Save(EngineerSettings settings)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, _options));
    }
}
