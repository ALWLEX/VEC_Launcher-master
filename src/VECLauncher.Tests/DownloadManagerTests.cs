using System.Text.Json;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.Tests;

public class DownloadProgressTests
{
    [Fact]
    public void Percent_WithBytes_ReturnsCorrectPercentage()
    {
        var progress = new DownloadProgress { BytesDone = 500, BytesTotal = 1000 };
        Assert.Equal(50.0, progress.Percent, 1);
    }

    [Fact]
    public void Percent_WithZeroTotal_ReturnsZero()
    {
        var progress = new DownloadProgress { BytesDone = 0, BytesTotal = 0 };
        Assert.Equal(0, progress.Percent);
    }

    [Fact]
    public void Percent_WithFilesOnly_ReturnsFilePercentage()
    {
        var progress = new DownloadProgress { FilesDone = 3, FilesTotal = 10 };
        Assert.Equal(30.0, progress.Percent, 1);
    }

    [Fact]
    public void Percent_ClampedTo100()
    {
        var progress = new DownloadProgress { BytesDone = 2000, BytesTotal = 1000 };
        Assert.Equal(100.0, progress.Percent, 1);
    }

    [Fact]
    public void Percent_BytesTakePriorityOverFiles()
    {
        var progress = new DownloadProgress
        {
            BytesDone = 250, BytesTotal = 1000,
            FilesDone = 9, FilesTotal = 10
        };
        Assert.Equal(25.0, progress.Percent, 1);
    }

    [Fact]
    public void DefaultValues_AreEmpty()
    {
        var progress = new DownloadProgress();
        Assert.Equal("", progress.Stage);
        Assert.Equal("", progress.CurrentFile);
        Assert.Equal(0, progress.BytesDone);
        Assert.Equal(0, progress.BytesTotal);
        Assert.Equal(0, progress.FilesDone);
        Assert.Equal(0, progress.FilesTotal);
    }
}

public class DownloadTaskTests
{
    [Fact]
    public void RequiredFields_MustBeSet()
    {
        var task = new DownloadTask
        {
            Url = "https://example.com/file.jar",
            TargetPath = "/tmp/file.jar"
        };
        Assert.Equal("https://example.com/file.jar", task.Url);
        Assert.Equal("/tmp/file.jar", task.TargetPath);
        Assert.Null(task.Sha1);
        Assert.Equal(0, task.Size);
    }

    [Fact]
    public void AllFields_CanBeSet()
    {
        var task = new DownloadTask
        {
            Url = "https://example.com/lib.jar",
            TargetPath = "/libs/lib.jar",
            Sha1 = "abc123",
            Size = 4096,
            Display = "lib.jar"
        };
        Assert.Equal("abc123", task.Sha1);
        Assert.Equal(4096, task.Size);
        Assert.Equal("lib.jar", task.Display);
    }
}

public class ResolveNativesExtractDirTests
{
    private static VersionDetail MakeDetail(string jvmArg)
    {
        var args = JsonSerializer.Serialize(new
        {
            jvm = new object[] { jvmArg }
        });
        return JsonSerializer.Deserialize<VersionDetail>(
            $"{{\"arguments\":{args}}}")!;
    }

    [Fact]
    public void NoJvmArgs_ReturnsDefault()
    {
        var detail = new VersionDetail();
        var result = DownloadManager.ResolveNativesExtractDir(detail, "/default");
        Assert.Equal("/default", result);
    }

    [Fact]
    public void NoNativesArg_ReturnsDefault()
    {
        var detail = MakeDetail("-Xmx2G");
        var result = DownloadManager.ResolveNativesExtractDir(detail, "/default");
        Assert.Equal("/default", result);
    }

    [Fact]
    public void WithNativesDirectory_ExpandsPath()
    {
        var detail = MakeDetail("-Djava.library.path=${natives_directory}/natives");
        var result = DownloadManager.ResolveNativesExtractDir(detail, "/natives_root");
        Assert.Contains("natives", result);
    }
}
