using System.Windows;
using ApexAI.Core.Configuration;

namespace ApexAI.Wpf;

public partial class SettingsWindow : Window
{
    public EngineerSettings Settings { get; private set; }
    public string ApiKey { get; private set; } = string.Empty;

    public SettingsWindow(EngineerSettings settings)
    {
        InitializeComponent();
        Settings = settings;
        ProviderBox.SelectedIndex = settings.Provider == EngineerProvider.Offline ? 0 : 1;
        EndpointBox.Text = settings.Endpoint;
        ModelBox.Text = settings.Model;
        PortBox.Text = settings.TelemetryPort.ToString();
        WidthBox.Text = settings.OverlayWidth.ToString("0");
        HeightBox.Text = settings.OverlayHeight.ToString("0");
        OpacityBox.Text = settings.OverlayOpacity.ToString("0.##");
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535)
        {
            MessageBox.Show("UDP port must be between 1 and 65535.", "Invalid settings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!double.TryParse(WidthBox.Text, out var width) || width is < 520 or > 1100 ||
            !double.TryParse(HeightBox.Text, out var height) || height is < 360 or > 800 ||
            !double.TryParse(OpacityBox.Text, out var opacity) || opacity is < 0.65 or > 1)
        {
            MessageBox.Show("Overlay width must be 520-1100, height 360-800, and opacity 0.65-1.",
                "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Settings = new EngineerSettings(
            ProviderBox.SelectedIndex == 1 ? EngineerProvider.OpenAiCompatible : EngineerProvider.Offline,
            string.IsNullOrWhiteSpace(EndpointBox.Text) ? new EngineerSettings().Endpoint : EndpointBox.Text.Trim(),
            string.IsNullOrWhiteSpace(ModelBox.Text) ? new EngineerSettings().Model : ModelBox.Text.Trim(), port,
            width, height, opacity);
        ApiKey = ApiKeyBox.Password;
        DialogResult = true;
    }
}
