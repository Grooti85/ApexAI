using ApexAI.Core.Race;

namespace ApexAI.Core.Engineer;

public interface IEngineerMessageService
{
    string GetMessage(RaceEvent raceEvent);
}

public sealed class DeterministicEngineerMessageService : IEngineerMessageService
{
    public string GetMessage(RaceEvent raceEvent) =>
        raceEvent.Type switch
        {
            RaceEventType.LowFuel => "Engineer: box soon or lift to reach the next safe window.",
            RaceEventType.TyreTemperatureHigh => "Engineer: manage slip and brake a little earlier.",
            RaceEventType.OffTrack => "Engineer: rejoin safely; reset your rhythm.",
            RaceEventType.Incident => "Engineer: incident noted. Build the next lap.",
            RaceEventType.LapCompleted => $"Engineer: {raceEvent.Message}",
            RaceEventType.SessionFinished => "Engineer: session complete.",
            RaceEventType.SessionConnected => "Engineer: telemetry is live.",
            _ => $"Engineer: {raceEvent.Message}"
        };
}
