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
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535)
        {
            MessageBox.Show("UDP port must be between 1 and 65535.", "Invalid settings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Settings = new EngineerSettings(
            ProviderBox.SelectedIndex == 1 ? EngineerProvider.OpenAiCompatible : EngineerProvider.Offline,
            string.IsNullOrWhiteSpace(EndpointBox.Text) ? new EngineerSettings().Endpoint : EndpointBox.Text.Trim(),
            string.IsNullOrWhiteSpace(ModelBox.Text) ? new EngineerSettings().Model : ModelBox.Text.Trim(), port);
        ApiKey = ApiKeyBox.Password;
        DialogResult = true;
    }
}
