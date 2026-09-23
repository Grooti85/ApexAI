using ApexAI.Core.Configuration;
using Xunit;

namespace ApexAI.Core.Tests;

public sealed class EngineerSettingsTests
{
    [Fact]
    public void PersistsOverlayLayoutAndOpacity()
    {
        var path = Path.Combine(Path.GetTempPath(), $"apexai-settings-{Guid.NewGuid():N}.json");
        try
        {
            var expected = new EngineerSettings(
                EngineerProvider.Offline,
                "https://example.test/v1/chat/completions",
                "test-model",
                9010,
                820,
                540,
                0.78);

            new EngineerSettingsStore(path).Save(expected);
            var actual = new EngineerSettingsStore(path).Load();

            Assert.Equal(expected, actual);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void UsesReadableOverlayDefaults()
    {
        var settings = new EngineerSettings();

        Assert.Equal(680, settings.OverlayWidth);
        Assert.Equal(460, settings.OverlayHeight);
        Assert.InRange(settings.OverlayOpacity, 0.65, 1);
    }
}
