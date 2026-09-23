namespace ApexAI.Core.Telemetry;

public enum SessionPhase { Garage, Practice, Qualifying, Race, Finished }

public sealed record TelemetrySnapshot(
    DateTimeOffset Timestamp,
    SessionPhase Phase,
    int LapNumber,
    double LapProgress,
    double SpeedKph,
    double FuelLiters,
    double FuelPerLapLiters,
    double TyreTemperatureCelsius,
    bool IsOffTrack,
    bool HasIncident,
    bool IsInPitLane,
    bool IsConnected);

public interface ITelemetryProvider
{
    TelemetrySnapshot Read();
}

public interface ITelemetryStream : IDisposable
{
    event EventHandler<TelemetrySnapshot>? SnapshotReceived;
    bool IsRunning { get; }
    string? LastError { get; }
    void Start();
    void Stop();
}

public sealed class MockTelemetryProvider : ITelemetryProvider
{
    private int _tick;

    public TelemetrySnapshot Read()
    {
        var tick = _tick++;
        var lap = 12 + tick / 30;
        return new TelemetrySnapshot(
            DateTimeOffset.UtcNow,
            SessionPhase.Race,
            lap,
            (tick % 30) / 30d,
            160 + (tick % 10) * 2,
            Math.Max(4, 42 - tick * 0.4),
            2.8,
            87 + (tick % 8),
            tick % 37 == 0,
            tick % 101 == 0,
            false,
            true);
    }
}
