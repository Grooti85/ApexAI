using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ApexAI.Core.Configuration;

public sealed record AccBroadcastingSetupResult(
    string ConfigPath,
    int Port,
    string? BackupPath,
    bool PasswordGenerated);

public sealed class AccBroadcastingSetupException : Exception
{
    public AccBroadcastingSetupException(string configPath, string? backupPath, bool configUpdated, Exception innerException)
        : base(configUpdated
            ? $"ACC settings were written to '{configPath}', but ApexAI could not protect the ACC credentials with Windows DPAPI."
            : $"ACC settings at '{configPath}' were not changed, but ApexAI could not protect the ACC credentials with Windows DPAPI.",
            innerException)
    {
        ConfigPath = configPath;
        BackupPath = backupPath;
        ConfigUpdated = configUpdated;
    }

    public string ConfigPath { get; }
    public string? BackupPath { get; }
    public bool ConfigUpdated { get; }
}

public sealed class AccBroadcastingSetupService
{
    public const int DefaultPort = 9000;
    public const string ConnectionSecretKey = "acc-connection-password";
    public const string CommandSecretKey = "acc-command-password";

    private readonly ISecretStore _secretStore;

    public AccBroadcastingSetupService(ISecretStore secretStore, string? documentsDirectory = null)
    {
        _secretStore = secretStore;
        var documents = documentsDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        ConfigPath = Path.Combine(documents, "Assetto Corsa Competizione", "Config", "broadcasting.json");
    }

    public string ConfigPath { get; }

    public AccBroadcastingSetupResult Configure()
    {
        if (!File.Exists(ConfigPath))
            throw new FileNotFoundException(
                $"ACC's broadcasting configuration was not found. Launch ACC once, close it, and try again. Expected path: {ConfigPath}",
                ConfigPath);

        var originalText = File.ReadAllText(ConfigPath);
        var root = JsonNode.Parse(originalText) as JsonObject
            ?? throw new JsonException("ACC broadcasting.json must contain a JSON object.");

        var portNode = root["updListenerPort"];
        var configuredPort = 0;
        var hasValidPort = portNode is JsonValue portValue
            && portValue.TryGetValue<int>(out configuredPort)
            && configuredPort is >= 1 and <= 65535;
        var port = hasValidPort ? configuredPort : DefaultPort;
        var portNeedsUpdate = !hasValidPort;

        var passwordNode = root["connectionPassword"];
        var configuredPassword = passwordNode is JsonValue passwordValue
            && passwordValue.TryGetValue<string>(out var passwordText)
            ? passwordText
            : null;
        string? commandPasswordText = null;
        var hasValidCommandPassword = root["commandPassword"] is JsonValue commandPasswordValue
            && commandPasswordValue.TryGetValue<string>(out commandPasswordText);
        var commandPassword = hasValidCommandPassword ? commandPasswordText! : string.Empty;
        var commandPasswordNeedsUpdate = !hasValidCommandPassword;
        var passwordNeedsUpdate = string.IsNullOrWhiteSpace(configuredPassword);
        var passwordGenerated = false;
        var password = configuredPassword;
        if (passwordNeedsUpdate)
        {
            password = _secretStore.Get(ConnectionSecretKey);
            if (string.IsNullOrWhiteSpace(password))
            {
                password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
                passwordGenerated = true;
            }
        }

        if (portNeedsUpdate)
            root["updListenerPort"] = port;
        if (passwordNeedsUpdate)
            root["connectionPassword"] = password;
        if (commandPasswordNeedsUpdate)
            root["commandPassword"] = commandPassword;

        string? backupPath = null;
        if (portNeedsUpdate || passwordNeedsUpdate || commandPasswordNeedsUpdate)
        {
            backupPath = CreateBackupPath();
            File.Copy(ConfigPath, backupPath, overwrite: false);

            var temporaryPath = ConfigPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporaryPath, ConfigPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        try
        {
            _secretStore.Set(ConnectionSecretKey, password!);
            if (string.IsNullOrEmpty(commandPassword))
                _secretStore.Remove(CommandSecretKey);
            else
                _secretStore.Set(CommandSecretKey, commandPassword);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception or CryptographicException or ArgumentException)
        {
            throw new AccBroadcastingSetupException(ConfigPath, backupPath,
                portNeedsUpdate || passwordNeedsUpdate || commandPasswordNeedsUpdate, exception);
        }

        return new AccBroadcastingSetupResult(ConfigPath, port, backupPath, passwordGenerated);
    }

    private string CreateBackupPath()
    {
        var directory = Path.GetDirectoryName(ConfigPath)!;
        var fileName = Path.GetFileName(ConfigPath);
        return Path.Combine(directory,
            $"{fileName}.apexai-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.bak");
    }
}
