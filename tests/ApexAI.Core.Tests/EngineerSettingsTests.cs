using ApexAI.Core.Configuration;
using ApexAI.Core.Engineer;
using System.Text;
using Xunit;

namespace ApexAI.Core.Tests;

public sealed class EngineerSettingsTests
{
    [Fact]
    public void PersistsOverlayLayoutAndOpacity()
    {
        var path = Path.Combine(Path.GetTempPath(), $"apexai-settings-{Guid.NewGuid():N}.json");
        try
        {
            var expected = new EngineerSettings(
                EngineerProvider.Offline,
                "https://example.test/v1/chat/completions",
                "test-model",
                9010,
                820,
                540,
                0.78);

            new EngineerSettingsStore(path).Save(expected);
            var actual = new EngineerSettingsStore(path).Load();

            Assert.Equal(expected, actual);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void UsesCompactOverlayDefaults()
    {
        var settings = new EngineerSettings();

        Assert.Equal(390, settings.OverlayWidth);
        Assert.Equal(185, settings.OverlayHeight);
        Assert.InRange(settings.OverlayOpacity, 0.65, 1);
    }

    [Fact]
    public void PersistsProviderConfigurationButNeverAnApiKey()
    {
        var path = Path.Combine(Path.GetTempPath(), $"apexai-settings-{Guid.NewGuid():N}.json");
        const string apiKey = "sk-test-must-not-be-in-settings";
        try
        {
            var settings = new EngineerSettings(
                EngineerProvider.OpenAiCompatible,
                "http://localhost:11434/v1/chat/completions",
                "qwen2.5",
                AiTimeoutSeconds: 75);
            new EngineerSettingsStore(path).Save(settings);

            var savedJson = File.ReadAllText(path);
            var restored = new EngineerSettingsStore(path).Load();

            Assert.Equal(settings, restored);
            Assert.DoesNotContain(apiKey, savedJson);
            Assert.DoesNotContain("ApiKey", savedJson, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(75, restored.AiTimeoutSeconds);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DpapiSecretStoreProtectsAndRemovesSecretsOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;

        var directory = Path.Combine(Path.GetTempPath(), $"apexai-secrets-{Guid.NewGuid():N}");
        const string key = "mentor-api-key";
        const string secret = "sk-private-test-value";
        try
        {
            var store = new DpapiSecretStore(directory);
            store.Set(key, secret);
            var protectedFile = File.ReadAllBytes(Path.Combine(directory, key + ".bin"));

            Assert.DoesNotContain(secret, Encoding.Latin1.GetString(protectedFile), StringComparison.Ordinal);
            Assert.Equal(secret, store.Get(key));
            store.Remove(key);
            Assert.Null(store.Get(key));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
