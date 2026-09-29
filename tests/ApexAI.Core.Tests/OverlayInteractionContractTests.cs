using Xunit;

namespace ApexAI.Core.Tests;

public sealed class OverlayInteractionContractTests
{
    [Fact]
    public void DashboardKeepsOptionalCompactOverlayDraggable()
    {
        var projectRoot = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(projectRoot, "src", "ApexAI.Wpf", "OverlayWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(projectRoot, "src", "ApexAI.Wpf", "OverlayWindow.xaml.cs"));
        var dashboard = File.ReadAllText(Path.Combine(projectRoot, "src", "ApexAI.Wpf", "MainWindow.xaml"));

        Assert.Contains("Background=\"Transparent\"", xaml);
        Assert.Contains("PreviewMouseLeftButtonDown=\"HeaderMouseLeftButtonDown\"", xaml);
        Assert.Contains("Content=\"Show live overlay\" Click=\"OverlayClick\"", dashboard);
        Assert.Contains("DragMove();", codeBehind);
        Assert.Contains("e.Handled = true;", codeBehind);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ApexAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate ApexAI.sln.");
    }
}
