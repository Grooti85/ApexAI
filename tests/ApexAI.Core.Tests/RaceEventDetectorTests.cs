using ApexAI.Core.Race;
using ApexAI.Core.Telemetry;
using Xunit;

namespace ApexAI.Core.Tests;

public sealed class RaceEventDetectorTests
{
    [Fact]
    public void DetectsLowFuelOnlyWhenCrossingThreshold()
    {
        var detector = new RaceEventDetector(lowFuelLaps: 2);
        detector.Detect(State(fuel: 8, lapFuel: 3.5));
        var events = detector.Detect(State(fuel: 6, lapFuel: 3.5));
        Assert.Contains(events, item => item.Type == RaceEventType.LowFuel);
        Assert.Empty(detector.Detect(State(fuel: 5.5, lapFuel: 3.5)).Where(item => item.Type == RaceEventType.LowFuel));
    }

    [Fact]
    public void EmitsOffTrackOnEdgeAndNotEverySample()
    {
        var detector = new RaceEventDetector();
        detector.Detect(State(offTrack: false));
        Assert.Contains(detector.Detect(State(offTrack: true)), item => item.Type == RaceEventType.OffTrack);
        Assert.DoesNotContain(detector.Detect(State(offTrack: true)), item => item.Type == RaceEventType.OffTrack);
    }

    [Fact]
    public void EmitsLapCompletedWhenLapNumberAdvances()
    {
        var detector = new RaceEventDetector();
        detector.Detect(State(lap: 4));
        Assert.Contains(detector.Detect(State(lap: 5)), item => item.Type == RaceEventType.LapCompleted);
    }

    private static RaceState State(int lap = 1, double fuel = 30, double lapFuel = 3, bool offTrack = false) =>
        new(DateTimeOffset.UtcNow, SessionPhase.Race, lap, .5, 100, fuel, lapFuel, 90, offTrack, false, false, true);
}
