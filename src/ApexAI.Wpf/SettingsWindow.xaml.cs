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

    public SettingsWindow(EngineerSettings settings)
    {
        InitializeComponent();
        Settings = settings;
        PortBox.Text = settings.TelemetryPort.ToString();
        WidthBox.Text = settings.OverlayWidth.ToString("0");
        HeightBox.Text = settings.OverlayHeight.ToString("0");
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
        Settings = Settings with
        {
            TelemetryPort = port,
            OverlayWidth = width,
            OverlayHeight = height
        };
        DialogResult = true;
    }
}
