namespace ApexAI.Core.Telemetry;

public enum SessionPhase { Garage, Practice, Qualifying, Race, Finished }
public enum TelemetrySource { AccBroadcasting, Demo }

public sealed record TelemetrySnapshot(
    DateTimeOffset Timestamp,
    SessionPhase Phase,
    int LapNumber,
    double? LapProgress,
    double? SpeedKph,
    double? FuelLiters,
    double? FuelPerLapLiters,
    double? TyreTemperatureCelsius,
    bool? IsOffTrack,
    bool? HasIncident,
    bool? IsInPitLane,
    bool IsConnected,
    TelemetrySource Source = TelemetrySource.AccBroadcasting,
    string? SessionId = null,
    string? TrackName = null,
    string? SessionType = null,
    int? LastLapTimeMs = null,
    int? BestLapTimeMs = null,
    bool? LastLapIsValid = null,
    bool? LastLapIsOutLap = null,
    bool? LastLapIsInLap = null);

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
            null,
            null,
            null,
            null,
            null,
            null,
            true,
            TelemetrySource.Demo,
            "demo-session",
            "Demo circuit",
            "Demo",
            100000 + (tick % 8) * 250,
            99000,
            true);
    }
}
