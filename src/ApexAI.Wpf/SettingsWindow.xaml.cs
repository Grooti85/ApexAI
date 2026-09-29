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
        ProviderBox.SelectedIndex = settings.Provider == EngineerProvider.OpenAiCompatible ? 1 : 0;
        EndpointBox.Text = settings.Endpoint;
        ModelBox.Text = settings.Model;
        TimeoutBox.Text = settings.AiTimeoutSeconds.ToString();
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
        var provider = ProviderBox.SelectedIndex == 1
            ? EngineerProvider.OpenAiCompatible
            : EngineerProvider.Offline;
        if (!int.TryParse(TimeoutBox.Text, out var timeout) || timeout is < 5 or > 180)
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
            Endpoint = EndpointBox.Text.Trim(),
            Model = ModelBox.Text.Trim(),
            AiTimeoutSeconds = timeout
        };
        DialogResult = true;
    }
}
