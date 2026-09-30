using ApexAI.Core.Telemetry;

namespace ApexAI.Core.Race;

public sealed class RaceEventDetector
{
    private readonly double _lowFuelLaps;
    private readonly double _highTyreTemperature;
    private RaceState? _previous;

    public RaceEventDetector(double lowFuelLaps = 2.0, double highTyreTemperature = 105)
    {
        if (lowFuelLaps <= 0) throw new ArgumentOutOfRangeException(nameof(lowFuelLaps));
        _lowFuelLaps = lowFuelLaps;
        _highTyreTemperature = highTyreTemperature;
    }

    public IReadOnlyList<RaceEvent> Detect(RaceState current)
    {
        var events = new List<RaceEvent>();
        if (_previous is null && current.IsConnected)
            events.Add(Create(RaceEventType.SessionConnected, current, "Telemetry connected."));
        if (_previous is not null && current.LapNumber > _previous.LapNumber)
            events.Add(Create(RaceEventType.LapCompleted, current, $"Lap {_previous.LapNumber} completed."));
        if (current.IsOffTrack == true && _previous?.IsOffTrack != true)
            events.Add(Create(RaceEventType.OffTrack, current, "Track limits: get back on line.", 2));
        if (current.HasIncident == true && _previous?.HasIncident != true)
            events.Add(Create(RaceEventType.Incident, current, "Incident detected. Stay focused.", 1));
        var lowFuel = current.FuelPerLapLiters is double fuelPerLap && fuelPerLap > 0 &&
                      current.FuelLiters is double fuel && fuel / fuelPerLap <= _lowFuelLaps;
        var wasLowFuel = _previous is not null &&
                         _previous.FuelPerLapLiters is double previousFuelPerLap && previousFuelPerLap > 0 &&
                         _previous.FuelLiters is double previousFuel &&
                         previousFuel / previousFuelPerLap <= _lowFuelLaps;
        if (lowFuel && !wasLowFuel)
            events.Add(Create(RaceEventType.LowFuel, current, "Fuel target is low: pit strategy required.", 1));
        if (current.TyreTemperatureCelsius is double tyreTemperature &&
            tyreTemperature >= _highTyreTemperature &&
            (_previous?.TyreTemperatureCelsius is not double previousTyreTemperature ||
             previousTyreTemperature < _highTyreTemperature))
            events.Add(Create(RaceEventType.TyreTemperatureHigh, current, "Tyres are overheating; protect the fronts.", 2));
        if (current.Phase == SessionPhase.Finished &&
            _previous?.Phase != SessionPhase.Finished)
            events.Add(Create(RaceEventType.SessionFinished, current, "Session finished."));
        _previous = current;
        return events;
    }

    private static RaceEvent Create(RaceEventType type, RaceState state, string message, int priority = 3) =>
        new(type, state.Timestamp, message, state.LapNumber, priority);
}
