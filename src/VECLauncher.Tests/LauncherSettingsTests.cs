using VECLauncher.Services;

namespace VECLauncher.Tests;

public class LauncherSettingsTests
{
    [Fact]
    public void RecommendedMaxMemory_ReturnsReasonableValue()
    {
        var result = LauncherSettings.RecommendedMaxMemory();

        // Must be between 2048 and 8192
        Assert.InRange(result, 2048, 8192);

        // Must be divisible by 512
        Assert.Equal(0, result % 512);
    }

    [Fact]
    public void RecommendedMaxMemory_IsStable()
    {
        var a = LauncherSettings.RecommendedMaxMemory();
        var b = LauncherSettings.RecommendedMaxMemory();
        Assert.Equal(a, b);
    }
}
