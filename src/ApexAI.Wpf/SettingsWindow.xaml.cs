using System.Windows;
using ApexAI.Core.Configuration;

namespace ApexAI.Wpf;

public partial class SettingsWindow : Window
{
    public EngineerSettings Settings { get; private set; }
    public string ConnectionPassword
    {
        get => ConnectionPasswordBox.Password;
        set => ConnectionPasswordBox.Password = value;
    }
    public string CommandPassword
    {
        get => CommandPasswordBox.Password;
        set => CommandPasswordBox.Password = value;
    }
    public string ApiKey
    {
        get => ApiKeyBox.Password;
        set => ApiKeyBox.Password = value;
    }

    public SettingsWindow(EngineerSettings settings)
    {
        InitializeComponent();
        Settings = settings;
        PortBox.Text = settings.TelemetryPort.ToString();
        WidthBox.Text = settings.OverlayWidth.ToString("0");
        HeightBox.Text = settings.OverlayHeight.ToString("0");
        ProviderBox.SelectedIndex = settings.Provider switch
        {
            EngineerProvider.Offline => 1,
            EngineerProvider.OpenAiCompatible => 2,
            _ => 0
        };
        EndpointBox.Text = settings.Provider == EngineerProvider.OpenAiCompatible ? settings.Endpoint : string.Empty;
        ModelBox.Text = settings.Provider == EngineerProvider.OpenAiCompatible ? settings.Model : string.Empty;
        TimeoutBox.Text = settings.AiTimeoutSeconds.ToString();
        ProviderBox.SelectionChanged += ProviderSelectionChanged;
        UpdateProviderFields();
    }

    private void ProviderSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        UpdateProviderFields();

    private void UpdateProviderFields()
    {
        HostedProviderPanel.Visibility = ProviderBox.SelectedIndex == 2
            ? Visibility.Visible : Visibility.Collapsed;
        LocalProviderNote.Visibility = ProviderBox.SelectedIndex == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535)
        {
            MessageBox.Show("UDP port must be between 1 and 65535.", "Invalid settings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!double.TryParse(WidthBox.Text, out var width) || width is < 320 or > 700 ||
            !double.TryParse(HeightBox.Text, out var height) || height is < 160 or > 500)
        {
            MessageBox.Show("Compact overlay width must be 320-700 and height 160-500.",
                "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var provider = ProviderBox.SelectedIndex switch
        {
            1 => EngineerProvider.Offline,
            2 => EngineerProvider.OpenAiCompatible,
            _ => EngineerProvider.LocalOllama
        };
        var timeout = Settings.AiTimeoutSeconds;
        if (provider == EngineerProvider.OpenAiCompatible &&
            (!int.TryParse(TimeoutBox.Text, out timeout) || timeout is < 5 or > 180))
        {
            MessageBox.Show("AI request timeout must be between 5 and 180 seconds.", "Invalid settings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (provider == EngineerProvider.OpenAiCompatible &&
            (!Uri.TryCreate(EndpointBox.Text, UriKind.Absolute, out var endpoint) ||
             endpoint.Scheme is not ("https" or "http") ||
             endpoint.Scheme == "http" && !endpoint.IsLoopback ||
             !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) ||
             !string.IsNullOrEmpty(endpoint.Fragment) || string.IsNullOrWhiteSpace(ModelBox.Text)))
        {
            MessageBox.Show("Enter a model and a valid HTTPS chat-completions URL, or loopback HTTP URL for a local provider; URL credentials and query parameters are not allowed.",
                "Invalid AI provider settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Settings = Settings with
        {
            TelemetryPort = port,
            OverlayWidth = width,
            OverlayHeight = height,
            Provider = provider,
            Endpoint = provider == EngineerProvider.LocalOllama
                ? "http://127.0.0.1:11434/v1/chat/completions" : EndpointBox.Text.Trim(),
            Model = provider == EngineerProvider.LocalOllama
                ? LocalAiSetupService.ModelName : ModelBox.Text.Trim(),
            AiTimeoutSeconds = timeout
        };
        DialogResult = true;
    }
}
