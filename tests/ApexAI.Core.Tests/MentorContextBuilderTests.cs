using ApexAI.Core.Engineer;
using ApexAI.Core.Sessions;
using Xunit;

namespace ApexAI.Core.Tests;

public sealed class MentorContextBuilderTests
{
    [Fact]
    public void BuildsLatestLapAnalysisFromRecordedSessionFactsAndNamesUnavailableTelemetry()
    {
        var older = Session("old", "Spa", true,
            new RecordedLap(1, 121_000, true, DateTimeOffset.Parse("2025-01-01T10:00:00Z")));
        var latest = Session("latest", "Spa", true,
            new RecordedLap(1, 120_500, true, DateTimeOffset.Parse("2025-01-02T10:00:00Z")),
            new RecordedLap(2, 121_000, true, DateTimeOffset.Parse("2025-01-02T10:01:00Z")),
            new RecordedLap(3, 122_000, false, DateTimeOffset.Parse("2025-01-02T10:02:00Z")));

        var context = MentorContextBuilder.Build([older, latest]);

        Assert.Contains("Persisted sessions: 2.", context);
        Assert.Contains("Lap 1: 2:00.500; valid", context);
        Assert.Contains("Latest session valid-lap personal best: 2:00.500", context);
        Assert.Contains("Previous completed-session personal best on this track: 2:01.000", context);
        Assert.Contains("invalid", context);
        Assert.Contains("fuel level, tyre temperatures, steering input, brake traces", context);
        Assert.Contains("Evidence-based next mission", context);
    }

    [Fact]
    public void StatesHistoryIsMissingRatherThanInventingNoSessionsWhenStorageCannotBeRead()
    {
        var context = MentorContextBuilder.Build([], historyUnavailable: true);

        Assert.Contains("local session store could not be read", context);
        Assert.DoesNotContain("there are no recorded sessions", context);
        Assert.Contains("unavailable", context);
    }

    [Fact]
    public void StatesNoSessionsAndUnavailableTelemetryWhenHistoryIsEmpty()
    {
        var context = MentorContextBuilder.Build([]);

        Assert.Contains("no persisted ACC sessions", context);
        Assert.Contains("Personalized driving coaching: unavailable until ACC session telemetry has been recorded", context);
        Assert.Contains("fuel, tyre temperatures", context);
        Assert.Contains("Latest lap analysis: unavailable", context);
    }

    [Fact]
    public void BoundsContextSizeForLargePersistedHistories()
    {
        var sessions = Enumerable.Range(0, 9)
            .Select(index => Session($"session-{index}", $"Track {index}", true,
                Enumerable.Range(1, 22).Select(lap => new RecordedLap(lap, 120_000, true,
                    DateTimeOffset.Parse("2025-01-02T10:00:00Z"))).ToArray()) with
            {
                StartedAt = DateTimeOffset.Parse("2025-01-02T10:00:00Z").AddMinutes(index)
            })
            .ToArray();

        var context = MentorContextBuilder.Build(sessions);

        Assert.Contains("Older sessions omitted from this context: 1.", context);
        Assert.Contains("Additional laps omitted: 2.", context);
        Assert.DoesNotContain("Track 0;", context);
    }

    private static RecordedSession Session(string id, string track, bool complete, params RecordedLap[] laps) =>
        new(id, track, "Practice", DateTimeOffset.Parse(id == "latest"
                ? "2025-01-02T10:00:00Z" : "2025-01-01T10:00:00Z"),
            DateTimeOffset.Parse("2025-01-02T10:10:00Z"), complete, laps);
}
