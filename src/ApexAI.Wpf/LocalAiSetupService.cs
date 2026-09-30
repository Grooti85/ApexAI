using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text.Json;
using ApexAI.Core.Configuration;

namespace ApexAI.Wpf;

internal enum LocalAiState { SetupRequired, NoModel, Ready, Error }

internal sealed record LocalAiStatus(LocalAiState State, string Message);
internal sealed record LocalAiSetupProgress(string Message, double? Percent = null);

internal sealed class LocalAiSetupException(string message) : Exception(message);

internal sealed class LocalAiSetupService : IDisposable
{
    public const string ModelName = "qwen2.5:3b";
    private const string InstallerUrl = "https://ollama.com/download/OllamaSetup.exe";
    private const string ApiBase = "http://127.0.0.1:11434";
    private const long MaximumInstallerBytes = 2L * 1024 * 1024 * 1024;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };
    private Process? _serverProcess;

    public LocalAiHardwareAssessment CheckHardware()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory))
            return new(false, "Windows could not report system memory. Local setup was stopped; use an optional hosted provider or try again.");

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var modelStorage = Environment.GetEnvironmentVariable("OLLAMA_MODELS");
        if (string.IsNullOrWhiteSpace(modelStorage))
            modelStorage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models");
        long freeBytes = long.MaxValue;
        foreach (var path in new[] { localAppData, modelStorage })
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrWhiteSpace(root))
                return new(false, "Windows could not identify the local disk for runtime or model storage. Check the Windows user profile and retry.");
            try { freeBytes = Math.Min(freeBytes, new DriveInfo(root).AvailableFreeSpace); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException)
            {
                return new(false, $"Windows could not check free disk space ({exception.Message}). Check the local disk and retry.");
            }
        }

        return LocalAiHardwareRequirements.Assess(
            Environment.Is64BitOperatingSystem, Avx2.IsSupported, memory.TotalPhysicalMemory, freeBytes);
    }

    public async Task<LocalAiStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (FindOllamaExecutable() is null)
            return new(LocalAiState.SetupRequired, "Setup required · the local Ollama runtime and model have not been installed.");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await _http.GetAsync($"{ApiBase}/api/tags", timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new(LocalAiState.Error, $"Ollama is installed but its local service returned HTTP {(int)response.StatusCode}. Retry setup or restart Ollama.");
            return await HasModelAsync(response, timeout.Token).ConfigureAwait(false)
                ? new(LocalAiState.Ready, $"Ready · {ModelName} is available in local Ollama. Mentor requests are sent to this device.")
                : new(LocalAiState.NoModel, $"No model · Ollama is running locally, but {ModelName} is not installed. Choose Set up local AI to download it.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(LocalAiState.Error, "Ollama is installed but its local service is not responding. Retry setup to start it.");
        }
        catch (HttpRequestException)
        {
            return new(LocalAiState.Error, "Ollama is installed but its local service is not responding. Retry setup to start it.");
        }
        catch (JsonException)
        {
            return new(LocalAiState.Error, "Ollama returned an invalid local model list. Restart Ollama and retry setup.");
        }
    }

    public async Task ProvisionAsync(IProgress<LocalAiSetupProgress> progress, CancellationToken cancellationToken)
    {
        var hardware = CheckHardware();
        if (!hardware.IsSupported)
            throw new LocalAiSetupException(hardware.Message);

        var executable = FindOllamaExecutable();
        if (executable is null)
        {
            var installer = await DownloadInstallerAsync(progress, cancellationToken).ConfigureAwait(false);
            try
            {
                progress.Report(new("Verifying the Ollama installer signature…"));
                VerifyInstallerSignature(installer, cancellationToken);
                progress.Report(new("Ollama installer verified. Complete the installer window to continue…"));
                using var process = Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true })
                    ?? throw new LocalAiSetupException("Windows could not start the verified Ollama installer. Retry or install Ollama from ollama.com.");
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                if (process.ExitCode != 0)
                    throw new LocalAiSetupException($"The Ollama installer exited with code {process.ExitCode}. Retry setup or install Ollama from ollama.com.");
                executable = await WaitForExecutableAsync(progress, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (File.Exists(installer)) File.Delete(installer);
            }
        }

        await EnsureServerReadyAsync(executable, progress, cancellationToken).ConfigureAwait(false);
        await PullModelAsync(progress, cancellationToken).ConfigureAwait(false);
        var status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.State != LocalAiState.Ready)
            throw new LocalAiSetupException($"Model setup did not finish successfully. {status.Message} Retry setup.");
    }

    private async Task<string> DownloadInstallerAsync(
        IProgress<LocalAiSetupProgress> progress, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ApexAI", "downloads");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "OllamaSetup.exe");
        var partialPath = path + ".partial";
        if (File.Exists(partialPath)) File.Delete(partialPath);

        progress.Report(new("Downloading the signed Ollama runtime…"));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, InstallerUrl);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
                throw new LocalAiSetupException("The Ollama installer download did not remain on HTTPS. Setup was stopped.");
            if (!response.IsSuccessStatusCode)
                throw new LocalAiSetupException($"Could not download the Ollama installer (HTTP {(int)response.StatusCode}). Check your connection and retry.");
            if (response.Content.Headers.ContentLength is > MaximumInstallerBytes)
                throw new LocalAiSetupException("The Ollama installer exceeded the 2 GB safety limit. Setup was stopped.");

            var total = response.Content.Headers.ContentLength;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[131072];
                long received = 0;
                while (true)
                {
                    var count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (count == 0) break;
                    received += count;
                    if (received > MaximumInstallerBytes)
                        throw new LocalAiSetupException("The Ollama installer exceeded the 2 GB safety limit. Setup was stopped.");
                    await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    progress.Report(new(
                        total is > 0 ? $"Downloading Ollama installer… {received / 1024d / 1024d:0} MB" : "Downloading Ollama installer…",
                        total is > 0 ? received * 100d / total.Value : null));
                }
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (total is > 0 && received != total.Value)
                    throw new LocalAiSetupException("The Ollama installer download was incomplete. Check your connection and retry.");
            }
            File.Move(partialPath, path, overwrite: true);
            return path;
        }
        catch
        {
            if (File.Exists(partialPath)) File.Delete(partialPath);
            throw;
        }
    }

    private static void VerifyInstallerSignature(string path, CancellationToken cancellationToken)
    {
        var powerShell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powerShell))
            throw new LocalAiSetupException("Windows PowerShell is unavailable to verify the installer signature. Setup was stopped.");

        var escapedPath = path.Replace("'", "''", StringComparison.Ordinal);
        var script = $"$signature = Get-AuthenticodeSignature -LiteralPath '{escapedPath}'; " +
                     "if ($signature.Status -eq 'Valid' -and $signature.SignerCertificate.Subject -match '(?i)(^|,)\\s*CN=Ollama(,?\\s*Inc\\.?)?(,|$)') { exit 0 } else { exit 1 }";
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = powerShell,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", script }
        }) ?? throw new LocalAiSetupException("Windows could not start signature verification. Setup was stopped.");
        process.WaitForExitAsync(cancellationToken).GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new LocalAiSetupException("Windows could not verify a valid Ollama publisher signature on the installer. The file was not run. Check your connection and retry.");
    }

    private static async Task<string> WaitForExecutableAsync(
        IProgress<LocalAiSetupProgress> progress, CancellationToken cancellationToken)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddMinutes(3);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var executable = FindOllamaExecutable();
            if (executable is not null) return executable;
            progress.Report(new("Waiting for the Ollama installation to finish…"));
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
        throw new LocalAiSetupException("Ollama was not found after installation. Finish or retry the Ollama installer, then retry setup.");
    }

    private async Task EnsureServerReadyAsync(
        string executable, IProgress<LocalAiSetupProgress> progress, CancellationToken cancellationToken)
    {
        if (await IsServerAvailableAsync(cancellationToken).ConfigureAwait(false)) return;
        progress.Report(new("Starting Ollama on this computer…"));
        try
        {
            if (_serverProcess is null || _serverProcess.HasExited)
                _serverProcess = Process.Start(new ProcessStartInfo(executable, "serve")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new LocalAiSetupException($"Could not start the local Ollama service ({exception.Message}). Retry setup or restart Ollama.");
        }
        if (_serverProcess is null)
            throw new LocalAiSetupException("Could not start the local Ollama service. Retry setup or restart Ollama.");

        var timeoutAt = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_serverProcess.HasExited)
                throw new LocalAiSetupException("Ollama stopped while starting its local service. Retry setup or reinstall Ollama.");
            if (await IsServerAvailableAsync(cancellationToken).ConfigureAwait(false)) return;
            progress.Report(new("Waiting for local Ollama to start…"));
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
        throw new LocalAiSetupException("Ollama did not start its local service in time. Retry setup or restart Ollama.");
    }

    private async Task<bool> IsServerAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await _http.GetAsync($"{ApiBase}/api/tags", timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
        catch (HttpRequestException) { return false; }
    }

    private async Task PullModelAsync(IProgress<LocalAiSetupProgress> progress, CancellationToken cancellationToken)
    {
        progress.Report(new($"Downloading {ModelName} from the Ollama registry…"));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/api/pull")
        {
            Content = JsonContent.Create(new PullRequest(ModelName, true))
        };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new LocalAiSetupException($"Ollama could not download {ModelName} (HTTP {(int)response.StatusCode}). Check your internet connection and retry.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var sawSuccess = false;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var item = JsonDocument.Parse(line);
            var root = item.RootElement;
            if (root.TryGetProperty("error", out var error))
                throw new LocalAiSetupException($"Ollama model download failed: {error}. Check your connection and disk space, then retry.");
            var status = root.TryGetProperty("status", out var statusValue) ? statusValue.GetString() : null;
            if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)) sawSuccess = true;
            var digest = root.TryGetProperty("digest", out var digestValue) ? digestValue.GetString() : null;
            var completed = root.TryGetProperty("completed", out var completedValue) && completedValue.TryGetInt64(out var bytesDone)
                ? bytesDone : 0;
            var total = root.TryGetProperty("total", out var totalValue) && totalValue.TryGetInt64(out var bytesTotal)
                ? bytesTotal : 0;
            var message = status is null ? $"Downloading {ModelName}…" :
                $"{status}{(string.IsNullOrWhiteSpace(digest) ? string.Empty : $" · SHA-256 {digest[..Math.Min(digest.Length, 16)]}…")}";
            progress.Report(new(message, total > 0 ? completed * 100d / total : null));
        }
        if (!sawSuccess)
            throw new LocalAiSetupException($"Ollama stopped before confirming the {ModelName} download. Retry setup.");
    }

    private static async Task<bool> HasModelAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            throw new JsonException("The local model list is missing.");
        return models.EnumerateArray().Any(model =>
            model.TryGetProperty("name", out var name) &&
            string.Equals(name.GetString(), ModelName, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindOllamaExecutable()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var installedPath = Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe");
        if (File.Exists(installedPath)) return installedPath;
        var path = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, "ollama.exe"))
            .FirstOrDefault(File.Exists);
        return path;
    }

    public void Dispose()
    {
        _http.Dispose();
        _serverProcess?.Dispose();
    }

    private sealed record PullRequest(string Model, bool Stream);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysicalMemory;
        public ulong AvailablePhysicalMemory;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);
}
