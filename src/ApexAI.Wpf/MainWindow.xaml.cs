using System.Net.Sockets;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ApexAI.Core.Configuration;
using ApexAI.Core.Sessions;
using ApexAI.Core.Telemetry;

namespace ApexAI.Wpf;

public partial class MainWindow : Window
{
    private readonly MockTelemetryProvider _demoTelemetry = new();
    private readonly DispatcherTimer _timer;
    private readonly EngineerSettingsStore _settingsStore = new();
    private readonly SessionRecorder? _sessionRecorder;
    private readonly string? _storageError;
    private AccUdpTelemetryStream _udpTelemetry;
    private TelemetrySnapshot? _latestLiveSnapshot;
    private EngineerSettings _settings;
    private OverlayWindow? _overlay;
    private bool _demoMode;
    private int _lastSessionRevision = -1;
    private string? _lastAutoSelectedCompletedSessionId;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsStore.Load();
        try
        {
            _sessionRecorder = new SessionRecorder(new SessionStore());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            _storageError = $"Session history unavailable: {exception.Message}";
        }

        _udpTelemetry = CreateTelemetryStream(_settings.TelemetryPort);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += UpdateDashboard;
        _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _udpTelemetry.Dispose();
            _overlay?.Close();
        };
        RefreshSessions();
        ShowSection("dashboard");
        UpdateDashboard(this, EventArgs.Empty);
    }

    private void UpdateDashboard(object? sender, EventArgs e)
    {
        var isLive = _latestLiveSnapshot is { } latest &&
                     DateTimeOffset.UtcNow - latest.Timestamp < TimeSpan.FromSeconds(2);
        if (isLive && _latestLiveSnapshot is { } liveSnapshot)
            RecordLiveSnapshot(liveSnapshot);
        var snapshot = _demoMode
            ? _demoTelemetry.Read()
            : isLive ? _latestLiveSnapshot : null;

        ConnectionText.Text = _demoMode ? "DEMO DATA" : isLive ? "LIVE ACC" : "WAITING FOR ACC";
        var statusColor = isLive && !_demoMode ? Color.FromRgb(71, 190, 125) :
            _demoMode ? Color.FromRgb(255, 183, 77) : Color.FromRgb(241, 56, 70);
        ConnectionText.Foreground = new SolidColorBrush(statusColor);
        ConnectionDot.Fill = new SolidColorBrush(statusColor);
        SourceLabel.Text = _demoMode ? "MOCK / DEMO DATA" : "ACC BROADCASTING";
        DemoButton.Content = _demoMode ? "Stop demo data" : "Use demo data";
        TransportText.Text = _storageError ??
            (_udpTelemetry.LastError is { } error
                ? $"ACC broadcast: {error}"
                : isLive ? $"Receiving ACC broadcast on UDP {_settings.TelemetryPort}"
                : $"No ACC broadcast packet received on UDP {_settings.TelemetryPort}");

        if (snapshot is null)
        {
            StatusText.Text = "Waiting for ACC broadcast telemetry";
            TrackText.Text = "No active session";
            SessionDetailText.Text = "Enable ACC broadcasting and start a session. This view only displays fields supplied by ACC.";
            LapText.Text = SpeedText.Text = BestLapText.Text = ProgressText.Text = "—";
            if (_overlay?.IsVisible == true) _overlay.UpdateSnapshot(null, false);
            return;
        }

        var demo = snapshot.Source == TelemetrySource.Demo;
        var trackName = snapshot.TrackName ?? (demo ? "Demo circuit" : "Unknown track");
        StatusText.Text = $"{snapshot.Phase}  ·  Lap {snapshot.LapNumber:00}";
        TrackText.Text = $"{trackName}  ·  {snapshot.SessionType ?? "Session"}{(demo ? "  ·  demo" : string.Empty)}";
        LapText.Text = snapshot.LapNumber.ToString("00");
        SpeedText.Text = snapshot.SpeedKph is double speed ? $"{speed:0}" : "—";
        BestLapText.Text = snapshot.BestLapTimeMs is int best ? FormatLap(best) : "—";
        ProgressText.Text = snapshot.LapProgress is double progress ? $"{progress:P0}" : "—";
        SessionDetailText.Text = snapshot.IsInPitLane == true
            ? "Car is in the pit lane."
            : $"Live lap timing and position from {(demo ? "the demo generator" : "ACC")}.";

        if (_overlay?.IsVisible == true) _overlay.UpdateSnapshot(snapshot, !demo);
    }

    private void RecordLiveSnapshot(TelemetrySnapshot snapshot)
    {
        if (_sessionRecorder is null) return;
        try
        {
            if (_sessionRecorder.Record(snapshot) is not null &&
                _sessionRecorder.Revision != _lastSessionRevision)
                RefreshSessions();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            TransportText.Text = $"Session could not be saved: {exception.Message}";
        }
    }

    private AccUdpTelemetryStream CreateTelemetryStream(int port)
    {
        var secrets = new DpapiSecretStore();
        var stream = new AccUdpTelemetryStream(port,
            secrets.Get("acc-connection-password") ?? string.Empty,
            secrets.Get("acc-command-password") ?? string.Empty);
        stream.SnapshotReceived += (_, snapshot) => Dispatcher.Invoke(() => _latestLiveSnapshot = snapshot);
        try
        {
            stream.Start();
        }
        catch (SocketException exception)
        {
            TransportText.Text = $"Could not start ACC broadcast client: {exception.Message}";
        }
        return stream;
    }

    private void RefreshSessions()
    {
        if (_sessionRecorder is null) return;
        var selectedId = (SessionList.SelectedItem as RecordedSession)?.Id;
        var sessions = _sessionRecorder.Sessions.OrderByDescending(session => session.StartedAt).ToArray();
        var latestCompleted = sessions.FirstOrDefault(session => session.IsComplete);
        var hasNewCompleted = latestCompleted is not null &&
                              latestCompleted.Id != _lastAutoSelectedCompletedSessionId;
        SessionList.ItemsSource = sessions;
        SessionList.SelectedItem = hasNewCompleted
            ? latestCompleted
            : sessions.FirstOrDefault(session => session.Id == selectedId) ?? latestCompleted;
        if (latestCompleted is not null) _lastAutoSelectedCompletedSessionId = latestCompleted.Id;
        _lastSessionRevision = _sessionRecorder.Revision;
        UpdateMission(latestCompleted is null ? null : SessionAnalysis.Create(latestCompleted, _sessionRecorder.Sessions));
        ShowReport(SessionList.SelectedItem as RecordedSession);
    }

    private void UpdateMission(SessionReport? report)
    {
        var title = report?.Mission.Title ?? "Collect your first live session";
        var why = report?.Mission.Why ?? "ApexAI will choose a measurable target from recorded ACC lap evidence.";
        MissionTitleText.Text = title;
        MissionWhyText.Text = why;
        MissionPageTitle.Text = title;
        MissionPageWhy.Text = why;
    }

    private void ShowReport(RecordedSession? session)
    {
        if (session is null || !session.IsComplete)
        {
            ReportTrackText.Text = "Select a completed session";
            ReportPbText.Text = "Personal best: —";
            ReportImprovementText.Text = "Improvement: —";
            ReportConsistencyText.Text = "Consistency: —";
            ReportQualityText.Text = "A report appears when ACC marks a session complete.";
            ReportMissionText.Text = "—";
            ReportMissionWhyText.Text = string.Empty;
            return;
        }

        var report = SessionAnalysis.Create(session, _sessionRecorder?.Sessions ?? []);
        ReportTrackText.Text = $"{session.TrackName} · {session.SessionType}";
        ReportPbText.Text = report.PersonalBestMs is int best
            ? $"Session PB: {FormatLap(best)}"
            : "Session PB: no valid timed laps";
        ReportImprovementText.Text = report.ImprovementMs is int improvement
            ? improvement > 0 ? $"Improvement: {improvement / 1000d:0.000}s faster than previous track PB"
                : improvement < 0 ? $"Improvement: {Math.Abs(improvement) / 1000d:0.000}s slower than previous track PB"
                : "Improvement: matched the previous track PB"
            : "Improvement: baseline; another session is needed for comparison";
        ReportConsistencyText.Text = report.ConsistencyStdDevSeconds is double spread
            ? $"Consistency: {spread:0.00}s lap-time standard deviation"
            : "Consistency: not enough valid laps to estimate";
        ReportQualityText.Text = report.DataQualitySummary;
        ReportMissionText.Text = report.Mission.Title;
        ReportMissionWhyText.Text = report.Mission.Why;
        UpdateMission(report);
    }

    private static string FormatLap(int milliseconds) =>
        TimeSpan.FromMilliseconds(milliseconds).ToString(@"m\:ss\.fff");

    private void ShowSection(string section)
    {
        DashboardPage.Visibility = section == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        SessionsPage.Visibility = section == "sessions" ? Visibility.Visible : Visibility.Collapsed;
        MissionPage.Visibility = section == "mission" ? Visibility.Visible : Visibility.Collapsed;
        DashboardNav.Foreground = section == "dashboard" ? Brushes.White : new SolidColorBrush(Color.FromRgb(183, 184, 192));
        SessionsNav.Foreground = section == "sessions" ? Brushes.White : new SolidColorBrush(Color.FromRgb(183, 184, 192));
        MissionNav.Foreground = section == "mission" ? Brushes.White : new SolidColorBrush(Color.FromRgb(183, 184, 192));
    }

    private void DashboardClick(object sender, RoutedEventArgs e) => ShowSection("dashboard");
    private void SessionsClick(object sender, RoutedEventArgs e) => ShowSection("sessions");
    private void MissionClick(object sender, RoutedEventArgs e) => ShowSection("mission");
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void DemoClick(object sender, RoutedEventArgs e) => _demoMode = !_demoMode;

    private void OverlayClick(object sender, RoutedEventArgs e)
    {
        if (_overlay?.IsVisible == true)
        {
            _overlay.Hide();
            return;
        }
        _overlay ??= new OverlayWindow(_settings.OverlayWidth, _settings.OverlayHeight, _settings.OverlayOpacity);
        _overlay.UpdateSnapshot(_demoMode ? _demoTelemetry.Read() : _latestLiveSnapshot,
            !_demoMode && _latestLiveSnapshot is not null &&
            DateTimeOffset.UtcNow - _latestLiveSnapshot.Timestamp < TimeSpan.FromSeconds(2));
        _overlay.Show();
    }

    private void SessionSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ShowReport(SessionList.SelectedItem as RecordedSession);

    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        var secretStore = new DpapiSecretStore();
        var oldConnectionPassword = secretStore.Get("acc-connection-password") ?? string.Empty;
        var oldCommandPassword = secretStore.Get("acc-command-password") ?? string.Empty;
        var dialog = new SettingsWindow(_settings)
        {
            Owner = this,
            ConnectionPassword = oldConnectionPassword,
            CommandPassword = oldCommandPassword
        };
        if (dialog.ShowDialog() != true) return;
        var oldPort = _settings.TelemetryPort;
        _settings = dialog.Settings;
        _settingsStore.Save(_settings);
        StorePassword(secretStore, "acc-connection-password", dialog.ConnectionPassword);
        StorePassword(secretStore, "acc-command-password", dialog.CommandPassword);
        _overlay?.SetSize(_settings.OverlayWidth, _settings.OverlayHeight);
        _overlay?.SetOpacity(_settings.OverlayOpacity);
        if (oldPort != _settings.TelemetryPort ||
            oldConnectionPassword != dialog.ConnectionPassword ||
            oldCommandPassword != dialog.CommandPassword)
        {
            _udpTelemetry.Dispose();
            _latestLiveSnapshot = null;
            _udpTelemetry = CreateTelemetryStream(_settings.TelemetryPort);
        }
    }

    private static void StorePassword(DpapiSecretStore secretStore, string name, string value)
    {
        if (string.IsNullOrEmpty(value)) secretStore.Remove(name);
        else secretStore.Set(name, value);
    }
}
