using static RvtIO.Parsing.TfBlockDeserializer;
using Xunit;

namespace RvtIO.Tests;

public class GElementPlaneWindingFallbackTests
{
    [Theory]
    [InlineData(-1.0, 0, -1)] [InlineData(0.0, 0, 0)] [InlineData(1.0, 0, 1)]
    [InlineData(-0.999, 0, -1)] [InlineData(0.001, 0, 0)] [InlineData(1.001, 0, 1)]
    [InlineData(2.0, 1, 0)] [InlineData(-2.0, 1, 0)] [InlineData(0.5, 1, 0)]
    public void ClassifierUsesNativeMinusZeroPlusOrder(double total, int code, int location)
    {
        var result = ClassifySourceWindingTotal(BitConverter.DoubleToInt64Bits(total), out var why);
        Assert.Null(why); Assert.NotNull(result); Assert.Equal(code, result.Code); Assert.Equal(location, result.Location);
    }

    [Theory]
    [InlineData(0.05)] [InlineData(-0.05)]
    public void ClassificationToleranceIsStrictAndAdjacentDoubleInsideIsAccepted(double boundary)
    {
        var outside = ClassifySourceWindingTotal(BitConverter.DoubleToInt64Bits(boundary), out _);
        double inside = boundary > 0 ? Math.BitDecrement(boundary) : Math.BitIncrement(boundary);
        var accepted = ClassifySourceWindingTotal(BitConverter.DoubleToInt64Bits(inside), out _);
        Assert.Equal(1, outside!.Code); Assert.Equal(0, accepted!.Code); Assert.Equal(0, accepted.Location);
    }

    [Fact]
    public void NonfiniteClassificationDoesNotBecomeNativeOutside()
    {
        Assert.Null(ClassifySourceWindingTotal(BitConverter.DoubleToInt64Bits(double.NaN), out var why));
        Assert.Equal("winding-classification", why);
    }
}
