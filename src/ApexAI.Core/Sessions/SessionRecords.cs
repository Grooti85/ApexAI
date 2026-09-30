using System.Text.Json;
using ApexAI.Core.Telemetry;

namespace ApexAI.Core.Sessions;

public sealed record RecordedLap(int LapNumber, int LapTimeMs, bool IsValid, DateTimeOffset RecordedAt);

public sealed record RecordedSession(
    string Id,
    string TrackName,
    string SessionType,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    bool IsComplete,
    IReadOnlyList<RecordedLap> Laps);

public sealed class SessionStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public SessionStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ApexAI", "sessions.json");
    }

    public IReadOnlyList<RecordedSession> LoadAll()
    {
        if (!File.Exists(_path)) return [];
        return JsonSerializer.Deserialize<List<RecordedSession>>(File.ReadAllText(_path), _options)
            ?? throw new InvalidDataException($"Session file '{_path}' contains no session data.");
    }

    public void SaveAll(IReadOnlyList<RecordedSession> sessions)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(sessions, _options));
        File.Move(temporaryPath, _path, true);
    }
}

public sealed class SessionRecorder
{
    private readonly SessionStore _store;
    private readonly List<RecordedSession> _sessions;
    private string? _activeSourceSessionId;
    private RecordedSession? _active;
    private bool _hasUnsavedChanges;

    public SessionRecorder(SessionStore store)
    {
        _store = store;
        _sessions = store.LoadAll().ToList();
    }

    public IReadOnlyList<RecordedSession> Sessions => _sessions;
    public RecordedSession? Latest => _sessions.OrderByDescending(session => session.StartedAt).FirstOrDefault();
    public int Revision { get; private set; }

    public RecordedSession? Record(TelemetrySnapshot snapshot)
    {
        if (snapshot.Source != TelemetrySource.AccBroadcasting || string.IsNullOrWhiteSpace(snapshot.SessionId) ||
            snapshot.Phase == SessionPhase.Garage)
            return null;

        var changed = false;
        if (_active is null || !string.Equals(_activeSourceSessionId, snapshot.SessionId, StringComparison.Ordinal))
        {
            _activeSourceSessionId = snapshot.SessionId;
            _active = new RecordedSession(
                $"{snapshot.SessionId}:{snapshot.Timestamp:yyyyMMddHHmmssfff}",
                snapshot.TrackName ?? "Unknown track",
                snapshot.SessionType ?? "Unknown",
                snapshot.Timestamp,
                snapshot.Timestamp,
                false,
                []);
            _sessions.Add(_active);
            changed = true;
        }

        var laps = _active.Laps.ToList();
        if (snapshot.LastLapTimeMs is > 0 && snapshot.LapNumber > 1 &&
            snapshot.LastLapIsOutLap != true && snapshot.LastLapIsInLap != true)
        {
            var lapNumber = snapshot.LapNumber - 1;
            if (!laps.Any(lap => lap.LapNumber == lapNumber))
            {
                laps.Add(new RecordedLap(lapNumber, snapshot.LastLapTimeMs.Value,
                    snapshot.LastLapIsValid == true, snapshot.Timestamp));
                changed = true;
            }
        }

        var complete = snapshot.Phase == SessionPhase.Finished || _active.IsComplete;
        var trackName = snapshot.TrackName ?? _active.TrackName;
        var sessionType = snapshot.SessionType ?? _active.SessionType;
        changed |= _active.IsComplete != complete || _active.TrackName != trackName ||
                   _active.SessionType != sessionType;
        _active = _active with
        {
            TrackName = trackName,
            SessionType = sessionType,
            UpdatedAt = snapshot.Timestamp,
            IsComplete = complete,
            Laps = laps
        };
        _sessions[_sessions.FindIndex(session => session.Id == _active.Id)] = _active;
        _hasUnsavedChanges |= changed;
        if (_hasUnsavedChanges)
        {
            _store.SaveAll(_sessions);
            _hasUnsavedChanges = false;
            Revision++;
        }
        return _active;
    }
}
