using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;

namespace ApexAI.Wpf;

public partial class LocalAiSetupWindow : Window
{
    private readonly LocalAiSetupService _setup;
    private CancellationTokenSource? _setupCancellation;
    private bool _isSettingUp;

    internal LocalAiSetupWindow(LocalAiSetupService setup)
    {
        InitializeComponent();
        _setup = setup;
        var hardware = setup.CheckHardware();
        HardwareText.Text = hardware.Message;
        HardwareText.Foreground = hardware.IsSupported
            ? new SolidColorBrush(Color.FromRgb(184, 197, 214))
            : new SolidColorBrush(Color.FromRgb(255, 112, 125));
        SetupButton.IsEnabled = hardware.IsSupported;
        Loaded += WindowLoaded;
        Closed += (_, _) => _setupCancellation?.Cancel();
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e) => await RefreshStatusAsync();

    private void NotNowClick(object sender, RoutedEventArgs e) => Close();

    private async Task RefreshStatusAsync()
    {
        try
        {
            var status = await _setup.GetStatusAsync();
            if (_isSettingUp) return;
            StatusText.Text = status.Message;
            if (status.State == LocalAiState.Ready)
            {
                SetupProgress.Value = 100;
                ProgressText.Text = "Setup complete.";
                SetupButton.Content = "Set up again";
            }
            else if (status.State == LocalAiState.Error)
            {
                ProgressText.Text = "Retry setup after following the guidance above.";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not inspect the local AI installation: {exception.Message}";
        }
    }

    private async void SetupClick(object sender, RoutedEventArgs e)
    {
        if (_isSettingUp)
        {
            _setupCancellation?.Cancel();
            return;
        }
        if (!ConsentCheck.IsChecked.GetValueOrDefault())
        {
            StatusText.Text = "Please confirm consent before downloading or installing anything.";
            return;
        }

        _isSettingUp = true;
        var setupCompleted = false;
        SetupButton.Content = "Cancel setup";
        CloseButton.IsEnabled = false;
        SetupProgress.Value = 0;
        _setupCancellation = new CancellationTokenSource();
        var progress = new Progress<LocalAiSetupProgress>(item =>
        {
            StatusText.Text = item.Message;
            if (item.Percent is double percent) SetupProgress.Value = Math.Clamp(percent, 0, 100);
            ProgressText.Text = item.Percent is double value ? $"{value:0}% complete" : string.Empty;
        });
        try
        {
            await _setup.ProvisionAsync(progress, _setupCancellation.Token);
            StatusText.Text = $"Ready · {LocalAiSetupService.ModelName} is installed and available in local Ollama.";
            ProgressText.Text = "No account, API key, or hosted AI call was used.";
            SetupProgress.Value = 100;
            setupCompleted = true;
        }
        catch (OperationCanceledException) when (_setupCancellation.IsCancellationRequested)
        {
            StatusText.Text = "Setup cancelled. An installer window already open may continue until you close it. Retry after it finishes to continue the model download.";
            ProgressText.Text = "ApexAI will not request another model download until you start setup again.";
        }
        catch (LocalAiSetupException exception)
        {
            StatusText.Text = exception.Message;
            ProgressText.Text = "Check the guidance above, then retry setup.";
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException
            or UnauthorizedAccessException or System.ComponentModel.Win32Exception
            or System.Text.Json.JsonException or InvalidOperationException)
        {
            StatusText.Text = $"Local AI setup failed: {exception.Message}";
            ProgressText.Text = "Check your connection, disk space, and Ollama installation, then retry.";
        }
        finally
        {
            _setupCancellation.Dispose();
            _setupCancellation = null;
            _isSettingUp = false;
            SetupButton.Content = setupCompleted ? "Set up again" : "Retry setup";
            CloseButton.IsEnabled = true;
        }
    }
}
