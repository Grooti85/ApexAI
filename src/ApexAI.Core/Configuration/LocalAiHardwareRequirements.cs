namespace ApexAI.Core.Configuration;

public sealed record LocalAiHardwareAssessment(bool IsSupported, string Message);

public static class LocalAiHardwareRequirements
{
    public const ulong MinimumMemoryBytes = 8UL * 1024 * 1024 * 1024;
    public const long MinimumFreeDiskBytes = 8L * 1024 * 1024 * 1024;

    public static LocalAiHardwareAssessment Assess(
        bool is64Bit, bool supportsAvx2, ulong totalMemoryBytes, long freeDiskBytes)
    {
        if (!is64Bit)
            return new(false, "Local AI setup requires 64-bit Windows. You can use an optional hosted provider instead.");
        if (!supportsAvx2)
            return new(false, "This setup requires an x64 processor with AVX2 support as a conservative local-inference minimum. Older CPUs may not run the model reliably; use an optional hosted provider or continue without AI.");
        if (totalMemoryBytes < MinimumMemoryBytes)
            return new(false, "Local AI setup requires at least 8 GB of system memory. This machine is below the supported minimum; use an optional hosted provider or continue without AI.");
        if (freeDiskBytes < MinimumFreeDiskBytes)
            return new(false, "Local AI setup needs at least 8 GB of free disk space for the runtime, model, and temporary downloads. Free space and retry, or use an optional hosted provider.");
        return new(true, "Minimum check passed: 64-bit Windows, at least 8 GB RAM, and at least 8 GB free disk. This is not a performance guarantee; CPU/GPU and available memory affect response speed.");
    }
}
