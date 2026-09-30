using System.Text.Json.Nodes;
using ApexAI.Core.Configuration;
using Xunit;

namespace ApexAI.Core.Tests;

public sealed class AccBroadcastingSetupServiceTests : IDisposable
{
    private readonly string _documentsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly FakeSecretStore _secretStore = new();

    [Fact]
    public void UpdatesInvalidRequiredValuesPreservesUnknownFieldsAndCreatesBackup()
    {
        var configPath = CreateConfig("""
            {
              "updListenerPort": 0,
              "connectionPassword": "",
              "commandPassword": "leave-me-alone",
              "futureSetting": { "enabled": true, "labels": ["keep", "these"] }
            }
            """);
        var original = File.ReadAllText(configPath);

        var result = new AccBroadcastingSetupService(_secretStore, _documentsDirectory).Configure();

        Assert.Equal(AccBroadcastingSetupService.DefaultPort, result.Port);
        Assert.True(result.PasswordGenerated);
        Assert.NotNull(result.BackupPath);
        Assert.Equal(original, File.ReadAllText(result.BackupPath!));
        var updated = JsonNode.Parse(File.ReadAllText(configPath))!.AsObject();
        Assert.Equal(AccBroadcastingSetupService.DefaultPort, updated["updListenerPort"]!.GetValue<int>());
        var password = updated["connectionPassword"]!.GetValue<string>();
        Assert.Matches("^[A-F0-9]{48}$", password);
        Assert.Equal(password, _secretStore.Get(AccBroadcastingSetupService.ConnectionSecretKey));
        Assert.Equal("leave-me-alone", updated["commandPassword"]!.GetValue<string>());
        Assert.Equal("leave-me-alone", _secretStore.Get(AccBroadcastingSetupService.CommandSecretKey));
        Assert.Equal("""{"enabled":true,"labels":["keep","these"]}""",
            updated["futureSetting"]!.ToJsonString());
    }

    [Fact]
    public void ReusesExistingValidPortAndPasswordWithoutChangingTheFile()
    {
        const string original = """{"updListenerPort":9123,"connectionPassword":"existing","commandPassword":"","unknown":7}""";
        var configPath = CreateConfig(original);

        var result = new AccBroadcastingSetupService(_secretStore, _documentsDirectory).Configure();

        Assert.Equal(9123, result.Port);
        Assert.Null(result.BackupPath);
        Assert.False(result.PasswordGenerated);
        Assert.Equal(original, File.ReadAllText(configPath));
        Assert.Equal("existing", _secretStore.Get(AccBroadcastingSetupService.ConnectionSecretKey));
    }

    [Fact]
    public void ReusesPreviouslyProtectedPasswordWhenConfigPasswordIsBlank()
    {
        CreateConfig("""{"updListenerPort":9000,"connectionPassword":"","commandPassword":""}""");
        _secretStore.Set(AccBroadcastingSetupService.ConnectionSecretKey, "protected-existing");

        var result = new AccBroadcastingSetupService(_secretStore, _documentsDirectory).Configure();

        Assert.False(result.PasswordGenerated);
        Assert.Equal("protected-existing", JsonNode.Parse(File.ReadAllText(result.ConfigPath))!["connectionPassword"]!.GetValue<string>());
        Assert.NotNull(result.BackupPath);
    }

    [Fact]
    public void MissingConfigIsNotCreatedAndReportsDetectedPath()
    {
        var service = new AccBroadcastingSetupService(_secretStore, _documentsDirectory);

        var exception = Assert.Throws<FileNotFoundException>(() => service.Configure());

        Assert.Equal(service.ConfigPath, exception.FileName);
        Assert.False(File.Exists(service.ConfigPath));
    }

    [Fact]
    public void InvalidJsonIsRejectedWithoutChangingTheFile()
    {
        var configPath = CreateConfig("{ invalid");

        Assert.ThrowsAny<System.Text.Json.JsonException>(
            () => new AccBroadcastingSetupService(_secretStore, _documentsDirectory).Configure());

        Assert.Equal("{ invalid", File.ReadAllText(configPath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(configPath)!, "*.bak"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_documentsDirectory))
            Directory.Delete(_documentsDirectory, recursive: true);
    }

    private string CreateConfig(string contents)
    {
        var configPath = new AccBroadcastingSetupService(_secretStore, _documentsDirectory).ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, contents);
        return configPath;
    }

    private sealed class FakeSecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _secrets = new();

        public void Set(string key, string value) => _secrets[key] = value;
        public string? Get(string key) => _secrets.GetValueOrDefault(key);
        public void Remove(string key) => _secrets.Remove(key);
    }
}
