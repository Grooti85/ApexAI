using System.Text.Json;

namespace ApexAI.Core.Configuration;

public enum EngineerProvider { Offline = 0, OpenAiCompatible = 1, LocalOllama = 2 }

public sealed record EngineerSettings(
    EngineerProvider Provider = EngineerProvider.LocalOllama,
    string Endpoint = "http://127.0.0.1:11434/v1/chat/completions",
    string Model = "qwen2.5:3b",
    int TelemetryPort = 9000,
    double OverlayWidth = 390,
    double OverlayHeight = 185,
    double OverlayOpacity = 0.94,
    int AiTimeoutSeconds = 180)
{
    public bool LocalAiSetupPromptSeen { get; init; }
}

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
