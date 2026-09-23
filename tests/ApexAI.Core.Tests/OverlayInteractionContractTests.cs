using Xunit;

namespace ApexAI.Core.Tests;

public sealed class OverlayInteractionContractTests
{
    [Fact]
    public void OverlayContractDocumentsHeaderDragAndFooterActions()
    {
        var projectRoot = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(projectRoot, "src", "ApexAI.Wpf", "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(projectRoot, "src", "ApexAI.Wpf", "MainWindow.xaml.cs"));

        Assert.Contains("Background=\"Transparent\"", xaml);
        Assert.Contains("PreviewMouseLeftButtonDown=\"HeaderMouseLeftButtonDown\"", xaml);
        Assert.Contains("Content=\"Settings\" Click=\"SettingsClick\"", xaml);
        Assert.Contains("Content=\"Close\" Click=\"CloseClick\"", xaml);
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
