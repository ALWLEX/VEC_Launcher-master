using VECLauncher.Services;

namespace VECLauncher.Tests;

public class PathValidatorTests
{
    [Theory]
    [InlineData("C:\\Users\\test\\file.txt", "C:\\Users\\test", true)]
    [InlineData("C:\\Users\\test\\sub\\file.txt", "C:\\Users\\test", true)]
    [InlineData("C:\\Windows\\file.txt", "C:\\Users\\test", false)]
    [InlineData("C:\\Users\\test\\..\\..\\Windows\\file.txt", "C:\\Users\\test", false)]
    public void IsPathSafe_DetectsTraversal(string path, string root, bool expected)
    {
        Assert.Equal(expected, PathValidator.IsPathSafe(path, root));
    }

    [Theory]
    [InlineData("file.txt", true)]
    [InlineData("my file.txt", true)]
    [InlineData("../../../etc/passwd", false)]
    [InlineData("file..name.txt", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsFilenameSafe_ValidatesCorrectly(string filename, bool expected)
    {
        Assert.Equal(expected, PathValidator.IsFilenameSafe(filename));
    }

    [Theory]
    [InlineData("hello.txt", "hello.txt")]
    [InlineData("file<>name.txt", "filename.txt")]
    [InlineData("../../../etc/passwd", "etcpasswd")]
    public void SanitizeFilename_RemovesInvalidChars(string input, string expected)
    {
        Assert.Equal(expected, PathValidator.SanitizeFilename(input));
    }

    [Fact]
    public void CombineSafe_ReturnsNullForTraversal()
    {
        var result = PathValidator.CombineSafe("C:\\root", "..", "..", "Windows", "file.txt");
        Assert.Null(result);
    }

    [Fact]
    public void CombineSafe_ReturnsPathWithinRoot()
    {
        var result = PathValidator.CombineSafe("C:\\root", "sub", "file.txt");
        Assert.NotNull(result);
        Assert.StartsWith("C:\\root", result!);
    }
}
