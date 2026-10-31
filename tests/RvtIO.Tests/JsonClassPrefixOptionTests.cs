using RvtIO.Cli;
using Xunit;

namespace RvtIO.Tests;

public class JsonClassPrefixOptionTests
{
    [Fact]
    public void WithoutTheOption_ThePrefixIsEmpty()
    {
        Assert.True(JsonClassPrefixOption.TryParse(["input.rvt", "output.json", "--json-full"], out string prefix, out string? error));
        Assert.Equal("", prefix);
        Assert.Null(error);
    }

    [Fact]
    public void TheValueAfterTheOption_IsThePrefix_WhereverTheOptionStands()
    {
        Assert.True(JsonClassPrefixOption.TryParse(["input.rvt", "output.json", "--json-class-prefix", "Cls"], out string prefix, out _));
        Assert.Equal("Cls", prefix);
        Assert.True(JsonClassPrefixOption.TryParse(["input.rvt", "--json-class-prefix", "Cls", "output.json", "--compact-json"], out prefix, out _));
        Assert.Equal("Cls", prefix);
    }

    [Fact]
    public void AnEmptyValue_IsAnEmptyPrefix()
    {
        Assert.True(JsonClassPrefixOption.TryParse(["input.rvt", "--json-class-prefix", ""], out string prefix, out string? error));
        Assert.Equal("", prefix);
        Assert.Null(error);
    }

    [Fact]
    public void TheOptionWithoutAValue_IsRefused()
    {
        Assert.False(JsonClassPrefixOption.TryParse(["input.rvt", "output.json", "--json-class-prefix"], out string prefix, out string? error));
        Assert.Equal("", prefix);
        Assert.Contains("--json-class-prefix", error);
    }

    [Fact]
    public void TheOptionTwice_IsRefused()
    {
        Assert.False(JsonClassPrefixOption.TryParse(["input.rvt", "--json-class-prefix", "A", "--json-class-prefix", "B"], out string prefix, out string? error));
        Assert.Equal("", prefix);
        Assert.Contains("exactly one value", error);
    }
}
