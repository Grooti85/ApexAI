using ApexAI.Core.Configuration;
using Xunit;

namespace ApexAI.Core.Tests;

public sealed class LocalAiHardwareRequirementsTests
{
    [Fact]
    public void Allows64BitDeviceAtDocumentedMemoryAndDiskMinimums()
    {
        var result = LocalAiHardwareRequirements.Assess(
            true, true, LocalAiHardwareRequirements.MinimumMemoryBytes,
            LocalAiHardwareRequirements.MinimumFreeDiskBytes);

        Assert.True(result.IsSupported);
        Assert.Contains("not a performance guarantee", result.Message);
    }

    [Fact]
    public void Rejects32BitDevice()
    {
        var result = LocalAiHardwareRequirements.Assess(
            false, true, LocalAiHardwareRequirements.MinimumMemoryBytes,
            LocalAiHardwareRequirements.MinimumFreeDiskBytes);

        Assert.False(result.IsSupported);
        Assert.Contains("64-bit Windows", result.Message);
    }

    [Fact]
    public void RejectsCpuWithoutAvx2()
    {
        var result = LocalAiHardwareRequirements.Assess(
            true, false, LocalAiHardwareRequirements.MinimumMemoryBytes,
            LocalAiHardwareRequirements.MinimumFreeDiskBytes);

        Assert.False(result.IsSupported);
        Assert.Contains("AVX2", result.Message);
    }

    [Fact]
    public void RejectsInsufficientMemoryAndDiskSpace()
    {
        var lowMemory = LocalAiHardwareRequirements.Assess(
            true, true, LocalAiHardwareRequirements.MinimumMemoryBytes - 1,
            LocalAiHardwareRequirements.MinimumFreeDiskBytes);
        var lowDisk = LocalAiHardwareRequirements.Assess(
            true, true, LocalAiHardwareRequirements.MinimumMemoryBytes,
            LocalAiHardwareRequirements.MinimumFreeDiskBytes - 1);

        Assert.False(lowMemory.IsSupported);
        Assert.Contains("8 GB of system memory", lowMemory.Message);
        Assert.False(lowDisk.IsSupported);
        Assert.Contains("8 GB of free disk space", lowDisk.Message);
    }
}
