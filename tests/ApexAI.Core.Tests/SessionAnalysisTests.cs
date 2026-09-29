using ApexAI.Core.Sessions;
using ApexAI.Core.Telemetry;
using Xunit;

namespace ApexAI.Core.Tests;

public sealed class SessionAnalysisTests
{
    [Fact]
    public void PersistsLiveLapsButNeverRecordsDemoTelemetry()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ApexAI.Tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(directory, "sessions.json");
        try
        {
            var recorder = new SessionRecorder(new SessionStore(file));
            Assert.Null(recorder.Record(Snapshot(TelemetrySource.Demo, lapNumber: 2, lapTime: 98000)));
            recorder.Record(Snapshot(TelemetrySource.AccBroadcasting, lapNumber: 2, lapTime: 98000));
            recorder.Record(Snapshot(TelemetrySource.AccBroadcasting, lapNumber: 2, lapTime: 98000));
            recorder.Record(Snapshot(TelemetrySource.AccBroadcasting, lapNumber: 3, lapTime: 101000, valid: false));
            recorder.Record(Snapshot(TelemetrySource.AccBroadcasting, lapNumber: 4, lapTime: 85000) with
            {
                LastLapIsOutLap = true
            });
            recorder.Record(Snapshot(TelemetrySource.AccBroadcasting, lapNumber: 4, lapTime: 85000) with
            {
                Phase = SessionPhase.Finished,
                LastLapIsOutLap = true
            });

            var saved = new SessionRecorder(new SessionStore(file));
            var session = Assert.Single(saved.Sessions);
            Assert.True(session.IsComplete);
            Assert.Equal(2, session.Laps.Count);
            Assert.Equal(98000, session.Laps[0].LapTimeMs);
            Assert.False(session.Laps[1].IsValid);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ReportsBestLapImprovementConsistencyQualityAndOneEvidenceBasedMission()
    {
        var previous = new RecordedSession("old", "Monza", "Race", DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(-1), true, [Lap(1, 98000)]);
        var unfinished = new RecordedSession("unfinished", "Monza", "Race", DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow, false, [Lap(1, 90000)]);
        var session = new RecordedSession("new", "Monza", "Race", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, true, [Lap(1, 97000), Lap(2, 100000), Lap(3, 99000)]);

        var report = SessionAnalysis.Create(session, [previous, unfinished]);

        Assert.Equal(97000, report.PersonalBestMs);
        Assert.Equal(98000, report.PreviousPersonalBestMs);
        Assert.Equal(1000, report.ImprovementMs);
        Assert.Equal(3, report.ValidLapCount);
        Assert.Equal(100, report.DataQualityPercent);
        Assert.NotNull(report.ConsistencyStdDevSeconds);
        Assert.Contains("within", report.Mission.Title);
        Assert.Contains("standard deviation", report.Mission.Why);
    }

    [Fact]
    public void ChoosesCleanLapMissionWhenInvalidLapsExist()
    {
        var session = new RecordedSession("s", "Spa", "Practice", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, true, [Lap(1, 100000), Lap(2, 120000, false)]);

        var report = SessionAnalysis.Create(session, []);

        Assert.Contains("without an invalid lap", report.Mission.Title);
        Assert.Contains("1 invalid", report.Mission.Why);
        Assert.Equal(50, report.DataQualityPercent);
    }

    private static RecordedLap Lap(int number, int timeMs, bool valid = true) =>
        new(number, timeMs, valid, DateTimeOffset.UtcNow);

    private static TelemetrySnapshot Snapshot(
        TelemetrySource source, int lapNumber, int lapTime, bool valid = true) =>
        new(DateTimeOffset.UtcNow, SessionPhase.Race, lapNumber, .5, 150, null, null, null,
            null, null, false, true, source, source == TelemetrySource.Demo ? "demo" : "live",
            "Monza", "Race", lapTime, null, valid);
}
