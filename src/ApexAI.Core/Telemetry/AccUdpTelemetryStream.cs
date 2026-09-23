using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;

namespace ApexAI.Core.Telemetry;

public sealed class AccUdpTelemetryStream : ITelemetryStream
{
    private readonly int _port;
    private UdpClient? _client;
    private CancellationTokenSource? _cancellation;
    private Task? _receiveTask;

    public AccUdpTelemetryStream(int port = 9000)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        _port = port;
    }

    public event EventHandler<TelemetrySnapshot>? SnapshotReceived;
    public bool IsRunning => _receiveTask is { IsCompleted: false };
    public string? LastError { get; private set; }

    public void Start()
    {
        if (IsRunning) return;
        try
        {
            _client = new UdpClient(_port);
            LastError = null;
        }
        catch (SocketException exception)
        {
            LastError = $"UDP port {_port} is unavailable: {exception.Message}";
            throw;
        }
        _cancellation = new CancellationTokenSource();
        var client = _client;
        _receiveTask = Task.Run(() => ReceiveLoopAsync(client, _cancellation.Token));
    }

    public void Stop()
    {
        _cancellation?.Cancel();
        _client?.Dispose();
        _client = null;
        _receiveTask = null;
        _cancellation?.Dispose();
        _cancellation = null;
    }

    public void Dispose() => Stop();

    private async Task ReceiveLoopAsync(UdpClient client, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                if (TryParse(result.Buffer, out var snapshot))
                    SnapshotReceived?.Invoke(this, snapshot);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
            catch (SocketException exception) when (!cancellationToken.IsCancellationRequested)
            {
                LastError = exception.Message;
                break;
            }
        }
    }

    internal static bool TryParse(ReadOnlySpan<byte> payload, out TelemetrySnapshot snapshot)
    {
        snapshot = default!;
        try
        {
            using var document = JsonDocument.Parse(payload.ToArray());
            var root = document.RootElement;
            var timestamp = root.TryGetProperty("timestamp", out var time)
                && DateTimeOffset.TryParse(time.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var parsedTime)
                ? parsedTime : DateTimeOffset.UtcNow;
            var phase = Enum.TryParse<SessionPhase>(root.GetProperty("phase").GetString(), true, out var parsedPhase)
                ? parsedPhase : SessionPhase.Garage;
            snapshot = new TelemetrySnapshot(timestamp, phase,
                root.GetProperty("lapNumber").GetInt32(),
                root.GetProperty("lapProgress").GetDouble(),
                root.GetProperty("speedKph").GetDouble(),
                root.GetProperty("fuelLiters").GetDouble(),
                root.GetProperty("fuelPerLapLiters").GetDouble(),
                root.GetProperty("tyreTemperatureCelsius").GetDouble(),
                root.GetProperty("isOffTrack").GetBoolean(),
                root.GetProperty("hasIncident").GetBoolean(),
                root.GetProperty("isInPitLane").GetBoolean(), true);
            return true;
        }
        catch (JsonException) { return false; }
        catch (KeyNotFoundException) { return false; }
        catch (FormatException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
