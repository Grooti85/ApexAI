using ApexAI.Core.Telemetry;

namespace ApexAI.Core.Race;

public enum RaceEventType
{
    SessionConnected,
    LapCompleted,
    OffTrack,
    Incident,
    LowFuel,
    TyreTemperatureHigh,
    SessionFinished
}

public sealed record RaceState(
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

public sealed record RaceEvent(
    RaceEventType Type,
    DateTimeOffset Timestamp,
    string Message,
    int LapNumber,
    int Priority);

public static class RaceStateMapper
{
    public static RaceState From(TelemetrySnapshot snapshot) =>
        new(snapshot.Timestamp, snapshot.Phase, snapshot.LapNumber, snapshot.LapProgress,
            snapshot.SpeedKph, snapshot.FuelLiters, snapshot.FuelPerLapLiters,
            snapshot.TyreTemperatureCelsius, snapshot.IsOffTrack, snapshot.HasIncident,
            snapshot.IsInPitLane, snapshot.IsConnected);
}
