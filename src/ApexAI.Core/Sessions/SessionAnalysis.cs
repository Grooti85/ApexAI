namespace ApexAI.Core.Sessions;

public sealed record NextMission(string Title, string Why);

public sealed record SessionReport(
    string TrackName,
    int ValidLapCount,
    int InvalidLapCount,
    int? PersonalBestMs,
    int? PreviousPersonalBestMs,
    int? ImprovementMs,
    double? ConsistencyStdDevSeconds,
    double DataQualityPercent,
    string DataQualitySummary,
    NextMission Mission);

public static class SessionAnalysis
{
    public static SessionReport Create(RecordedSession session, IEnumerable<RecordedSession> history)
    {
        var validLaps = session.Laps.Where(lap => lap.IsValid && lap.LapTimeMs > 0).ToArray();
        var invalidCount = session.Laps.Count(lap => !lap.IsValid);
        var personalBest = validLaps.Length == 0 ? (int?)null : validLaps.Min(lap => lap.LapTimeMs);
        var previousPersonalBest = history
            .Where(previous => previous.IsComplete && previous.Id != session.Id &&
                string.Equals(previous.TrackName, session.TrackName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(previous => previous.Laps)
            .Where(lap => lap.IsValid && lap.LapTimeMs > 0)
            .Select(lap => (int?)lap.LapTimeMs)
            .Min();
        var improvement = personalBest is not null && previousPersonalBest is not null
            ? previousPersonalBest - personalBest
            : null;
        var mean = validLaps.Length == 0 ? 0 : validLaps.Average(lap => lap.LapTimeMs);
        var stdDev = validLaps.Length < 2
            ? (double?)null
            : Math.Sqrt(validLaps.Average(lap => Math.Pow(lap.LapTimeMs - mean, 2))) / 1000d;
        var quality = session.Laps.Count == 0 ? 0 : 100d * validLaps.Length / session.Laps.Count;
        var qualitySummary = session.Laps.Count == 0
            ? "No completed laps recorded; lap analysis is not available."
            : $"{validLaps.Length} valid and {invalidCount} invalid completed laps recorded ({quality:0}% valid).";
        var mission = ChooseMission(validLaps.Length, invalidCount, personalBest, stdDev);

        return new SessionReport(session.TrackName, validLaps.Length, invalidCount,
            personalBest, previousPersonalBest, improvement, stdDev, quality,
            qualitySummary, mission);
    }

    private static NextMission ChooseMission(int validLapCount, int invalidLapCount, int? personalBest, double? stdDev)
    {
        if (invalidLapCount > 0)
            return new NextMission("Complete the next 3 laps without an invalid lap.",
                $"This session recorded {invalidLapCount} invalid lap(s); prioritize clean laps before pace.");
        if (validLapCount < 3)
            return new NextMission("Record 3 valid timed laps in your next session.",
                $"Only {validLapCount} valid timed lap(s) are available, not enough to assess consistency.");
        if (personalBest is null || stdDev is null)
            return new NextMission("Record more valid timed laps.",
                "A reliable lap-time consistency estimate needs multiple valid lap times.");

        var toleranceSeconds = Math.Max(0.5, stdDev.Value);
        return new NextMission($"Run 5 valid laps within {toleranceSeconds:0.0}s of your personal best.",
            $"The {validLapCount} valid laps have a {stdDev.Value:0.00}s standard deviation; use that measured spread as your target.");
    }
}
