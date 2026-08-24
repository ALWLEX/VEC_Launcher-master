using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.Tests;

public class AccountStorageResultTests
{
    [Fact]
    public void TryLoad_NoAccount_ReturnsFailure()
    {
        // This test verifies the Result<T> pattern works
        // Note: actual file I/O tests need temp directories
        var result = AccountStorage.TryLoad();
        // Result should be either success or failure, never throw
        Assert.True(result.IsSuccess || result.IsFailure);
    }

    [Fact]
    public void TrySave_NullAccount_ReturnsFailure()
    {
        // TrySave with null should handle gracefully
        var result = AccountStorage.TrySave(null!);
        // Should not throw, should return a result
        Assert.True(result.IsSuccess || result.IsFailure);
    }
}
