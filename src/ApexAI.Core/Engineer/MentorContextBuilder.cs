using System.Globalization;
using ApexAI.Core.Sessions;

namespace ApexAI.Core.Engineer;

public static class MentorContextBuilder
{
    private const int MaximumSessions = 8;
    private const int MaximumLapsPerSession = 20;

    public static string Build(IReadOnlyList<RecordedSession> sessions, bool historyUnavailable = false)
    {
        var ordered = sessions.OrderByDescending(session => session.StartedAt).ToArray();
        if (ordered.Length == 0)
        {
            if (historyUnavailable)
            {
                return """
                    Recorded ACC session history: unavailable because the local session store could not be read; do not assume no sessions exist.
                    Latest lap analysis: unavailable because session history could not be read.
                    Telemetry limitations: fuel, tyre temperatures, steering, brake traces, and corner-by-corner telemetry are unavailable from the supported ACC broadcast feed.
                    """;
            }
            return """
                Recorded ACC session history: unavailable; no persisted ACC sessions have been recorded.
                Latest lap analysis: unavailable; there are no recorded sessions.
                Telemetry limitations: fuel, tyre temperatures, steering, brake traces, and corner-by-corner telemetry are unavailable from the supported ACC broadcast feed.
                """;
        }

        var latest = ordered[0];
        var report = SessionAnalysis.Create(latest, sessions);
        var lines = new List<string>
        {
            "Recorded ACC session facts (local session history; newest first):",
            $"Persisted sessions: {ordered.Length}."
        };

        foreach (var session in ordered.Take(MaximumSessions))
        {
            lines.Add($"- {session.TrackName}; {session.SessionType}; started {session.StartedAt.ToString("u", CultureInfo.InvariantCulture)}; " +
                      $"{(session.IsComplete ? "complete" : "in progress")}; {session.Laps.Count} recorded timed laps.");
            foreach (var lap in session.Laps.OrderBy(lap => lap.LapNumber).Take(MaximumLapsPerSession))
            {
                lines.Add($"  Lap {lap.LapNumber}: {FormatLap(lap.LapTimeMs)}; {(lap.IsValid ? "valid" : "invalid")}.");
            }
            if (session.Laps.Count > MaximumLapsPerSession)
                lines.Add($"  Additional laps omitted: {session.Laps.Count - MaximumLapsPerSession}.");
        }
        if (ordered.Length > MaximumSessions)
            lines.Add($"Older sessions omitted from this context: {ordered.Length - MaximumSessions}.");

        lines.Add($"Latest session analysis for {report.TrackName}: {report.DataQualitySummary}");
        lines.Add(report.PersonalBestMs is int best
            ? $"Latest session valid-lap personal best: {FormatLap(best)}."
            : "Latest session personal best: unavailable; no valid timed laps.");
        lines.Add(report.PreviousPersonalBestMs is int previous
            ? $"Previous completed-session personal best on this track: {FormatLap(previous)}."
            : "Previous completed-session personal best on this track: unavailable; no comparable completed session.");
        lines.Add(report.ImprovementMs is int delta
            ? $"Change against previous track personal best: {delta / 1000d:+0.000;-0.000;0.000} seconds (positive means faster)."
            : "Change against previous track personal best: unavailable; no comparison can be made.");
        lines.Add(report.ConsistencyStdDevSeconds is double spread
            ? $"Latest session valid-lap standard deviation: {spread.ToString("0.000", CultureInfo.InvariantCulture)} seconds."
            : "Latest session consistency estimate: unavailable; fewer than two valid timed laps.");
        lines.Add($"Evidence-based next mission: {report.Mission.Title} Reason: {report.Mission.Why}");
        lines.Add("Unavailable telemetry: fuel level, tyre temperatures, steering input, brake traces, and corner-by-corner data. Do not infer these values or claim advice is derived from them.");
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatLap(int milliseconds) =>
        TimeSpan.FromMilliseconds(milliseconds).ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);
}
