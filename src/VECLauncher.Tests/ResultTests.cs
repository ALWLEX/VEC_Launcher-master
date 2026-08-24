using VECLauncher.Services;

namespace VECLauncher.Tests;

public class ResultTests
{
    [Fact]
    public void Ok_CreatesSuccessResult()
    {
        var result = Result<string>.Ok("hello");
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal("hello", result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Fail_CreatesFailureResult()
    {
        var result = Result<string>.Fail("error");
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal("error", result.Error);
    }

    [Fact]
    public void Map_TransformsValueOnSuccess()
    {
        var result = Result<int>.Ok(5);
        var mapped = result.Map(x => x * 2);
        Assert.True(mapped.IsSuccess);
        Assert.Equal(10, mapped.Value);
    }

    [Fact]
    public void Map_PropagatesFailure()
    {
        var result = Result<int>.Fail("error");
        var mapped = result.Map(x => x * 2);
        Assert.True(mapped.IsFailure);
        Assert.Equal("error", mapped.Error);
    }

    [Fact]
    public void Bind_ChainsOperations()
    {
        var result = Result<int>.Ok(5);
        var chained = result.Bind(x => Result<string>.Ok($"Value: {x}"));
        Assert.True(chained.IsSuccess);
        Assert.Equal("Value: 5", chained.Value);
    }

    [Fact]
    public void Bind_PropagatesFailure()
    {
        var result = Result<int>.Fail("error");
        var chained = result.Bind(x => Result<string>.Ok($"Value: {x}"));
        Assert.True(chained.IsFailure);
        Assert.Equal("error", chained.Error);
    }

    [Fact]
    public void Unwrap_ReturnsValueOnSuccess()
    {
        var result = Result<int>.Ok(42);
        Assert.Equal(42, result.Unwrap());
    }

    [Fact]
    public void Unwrap_ThrowsOnFailure()
    {
        var result = Result<int>.Fail("error");
        Assert.Throws<InvalidOperationException>(() => result.Unwrap());
    }

    [Fact]
    public void UnwrapOrDefault_ReturnsProvidedDefaultOnFailure()
    {
        var result = Result<int>.Fail("error");
        Assert.Equal(99, result.UnwrapOrDefault(99));
    }

    [Fact]
    public void ImplicitConversion_FromValue()
    {
        Result<int> result = 42;
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void NonGenericResult_Ok()
    {
        var result = Result.Ok();
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void NonGenericResult_Fail()
    {
        var result = Result.Fail("error");
        Assert.True(result.IsFailure);
        Assert.Equal("error", result.Error);
    }

    [Fact]
    public void ToString_Ok()
    {
        var result = Result<int>.Ok(42);
        Assert.Equal("Ok(42)", result.ToString());
    }

    [Fact]
    public void ToString_Fail()
    {
        var result = Result<int>.Fail("error");
        Assert.Equal("Fail(error)", result.ToString());
    }
}
