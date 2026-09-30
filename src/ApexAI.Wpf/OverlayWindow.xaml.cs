using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ApexAI.Core.Telemetry;

namespace ApexAI.Wpf;

public partial class OverlayWindow : Window
{
    public OverlayWindow(double width, double height, double opacity)
    {
        InitializeComponent();
        SetSize(width, height);
        SetOpacity(opacity);
    }

    public void SetSize(double width, double height)
    {
        Width = Math.Clamp(width, 320, 700);
        Height = Math.Clamp(height, 160, 500);
    }

    public void SetOpacity(double opacity) => Opacity = Math.Clamp(opacity, 0.65, 1);

    public void UpdateSnapshot(TelemetrySnapshot? snapshot, bool isLive)
    {
        ConnectionText.Text = isLive ? "LIVE ACC" : snapshot?.Source == TelemetrySource.Demo ? "DEMO DATA" : "WAITING";
        var color = isLive ? Color.FromRgb(71, 190, 125) :
            snapshot?.Source == TelemetrySource.Demo ? Color.FromRgb(255, 183, 77) : Color.FromRgb(241, 56, 70);
        ConnectionText.Foreground = new SolidColorBrush(color);
        ConnectionDot.Fill = new SolidColorBrush(color);
        LapText.Text = snapshot?.LapNumber.ToString("00") ?? "—";
        SpeedText.Text = snapshot?.SpeedKph is double speed ? $"{speed:0}" : "—";
        BestText.Text = snapshot?.BestLapTimeMs is int best
            ? TimeSpan.FromMilliseconds(best).ToString(@"m\:ss\.fff")
            : "—";
    }

    private void HeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        DragMove();
        e.Handled = true;
    }

    private void HideClick(object sender, RoutedEventArgs e) => Hide();
}
