using System.Net.Sockets;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ApexAI.Core.Configuration;
using ApexAI.Core.Engineer;
using ApexAI.Core.Race;
using ApexAI.Core.Telemetry;

namespace ApexAI.Wpf;

public partial class MainWindow : Window
{
    private readonly MockTelemetryProvider _mockTelemetry = new();
    private AccUdpTelemetryStream _udpTelemetry;
    private TelemetrySnapshot _latestSnapshot;
    private readonly RaceEventDetector _detector = new();
    private IEngineerMessageService _engineer;
    private EngineerSettings _settings;
    private readonly EngineerSettingsStore _settingsStore = new();
    private readonly DispatcherTimer _timer;
    private bool _messageRequestInFlight;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsStore.Load();
        Width = Math.Clamp(_settings.OverlayWidth, 520, 1100);
        Height = Math.Clamp(_settings.OverlayHeight, 360, 800);
        Opacity = Math.Clamp(_settings.OverlayOpacity, 0.65, 1);
        _latestSnapshot = _mockTelemetry.Read();
        _udpTelemetry = CreateTelemetryStream(_settings.TelemetryPort);
        _engineer = CreateEngineerService(_settings);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += UpdateOverlay;
        _timer.Start();
        Closed += (_, _) => _udpTelemetry.Dispose();
    }

    private void UpdateOverlay(object? sender, EventArgs e)
    {
        var snapshot = _udpTelemetry.IsRunning ? _latestSnapshot : _mockTelemetry.Read();
        var state = RaceStateMapper.From(snapshot);
        var packetAge = DateTimeOffset.UtcNow - state.Timestamp;
        var live = _udpTelemetry.IsRunning && packetAge < TimeSpan.FromSeconds(2);
        ConnectionText.Text = live ? "ACC CONNECTED" : "MOCK MODE";
        var statusBrush = live ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.Orange;
        ConnectionText.Foreground = statusBrush;
        ConnectionDot.Fill = statusBrush;
        var transportStatus = _udpTelemetry.LastError is null
            ? $"Listening for ACC UDP on {_settings.TelemetryPort}"
            : $"ACC UDP unavailable; using mock telemetry ({_udpTelemetry.LastError})";
        TransportText.Text = transportStatus;
        ModeText.Text = _settings.Provider == EngineerProvider.Offline ? "OFFLINE\nENGINEER" : "AI ENGINEER\n+ FALLBACK";
        StatusText.Text = $"{state.Phase}  •  Lap {state.LapNumber}  •  {state.SpeedKph:0} km/h";
        SpeedText.Text = $"{state.SpeedKph:0}";
        FuelText.Text = $"{state.FuelLiters:0.0}";
        TyreText.Text = $"{state.TyreTemperatureCelsius:0}";
        LapText.Text = $"{state.LapNumber:00}";
        ProgressText.Text = $"{state.LapProgress:P0} complete";
        var raceEvent = _detector.Detect(state).OrderBy(item => item.Priority).FirstOrDefault();
        if (raceEvent is null)
        {
            EngineerText.Text = "Engineer: all clear.";
        }
        else if (!_messageRequestInFlight)
        {
            _messageRequestInFlight = true;
            EngineerText.Text = "Engineer: checking...";
            _ = ResolveEngineerMessageAsync(raceEvent);
        }
    }

    private static IEngineerMessageService CreateEngineerService(EngineerSettings settings)
    {
        if (settings.Provider != EngineerProvider.OpenAiCompatible) return new DeterministicEngineerMessageService();
        var apiKey = new DpapiSecretStore().Get("engineer-api-key");
        return string.IsNullOrWhiteSpace(apiKey)
            ? new DeterministicEngineerMessageService()
            : new OpenAiCompatibleEngineerMessageService(new HttpClient { Timeout = TimeSpan.FromSeconds(3) }, settings, apiKey);
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private void HeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private async Task ResolveEngineerMessageAsync(RaceEvent raceEvent)
    {
        try
        {
            var message = await Task.Run(() => _engineer.GetMessage(raceEvent));
            await Dispatcher.InvokeAsync(() =>
            {
                if (IsVisible) EngineerText.Text = message;
            });
        }
        finally
        {
            await Dispatcher.InvokeAsync(() => _messageRequestInFlight = false);
        }
    }

    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings);
        if (dialog.ShowDialog() != true) return;
        _settings = dialog.Settings;
        _settingsStore.Save(_settings);
        Width = Math.Clamp(_settings.OverlayWidth, 520, 1100);
        Height = Math.Clamp(_settings.OverlayHeight, 360, 800);
        Opacity = Math.Clamp(_settings.OverlayOpacity, 0.65, 1);
        if (!string.IsNullOrWhiteSpace(dialog.ApiKey))
            new DpapiSecretStore().Set("engineer-api-key", dialog.ApiKey);
        _engineer = CreateEngineerService(_settings);
        _udpTelemetry.Dispose();
        _udpTelemetry = CreateTelemetryStream(_settings.TelemetryPort);
    }

    private AccUdpTelemetryStream CreateTelemetryStream(int port)
    {
        var stream = new AccUdpTelemetryStream(port);
        stream.SnapshotReceived += (_, snapshot) => Dispatcher.Invoke(() => _latestSnapshot = snapshot);
        try { stream.Start(); }
        catch (SocketException) { }
        return stream;
    }
}
