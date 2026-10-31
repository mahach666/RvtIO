using static RvtIO.Parsing.TfBlockDeserializer;
using Xunit;

namespace RvtIO.Tests;

public class SourceWindingSegmentIntegralTests
{
    [Theory]
    [InlineData(true, false, 0.25)] [InlineData(true, true, -0.25)]
    [InlineData(false, false, -0.25)] [InlineData(false, true, 0.25)]
    public void WindingSegmentCombinesOrientationAndNativeParameterReversal(bool orientation, bool reverse, double expected)
    {
        var value = IntegrateSourceWindingSegment(Bits(1, -1), Bits(1, 1), Bits(0, 0), orientation, reverse, 1e-6, out var why);
        Assert.Null(why); Assert.NotNull(value); Assert.Equal(0, value.Code);
        Assert.InRange(Math.Abs(BitConverter.Int64BitsToDouble(value.ValueBits!.Value) - expected), 0, 1e-6);
    }

    [Fact]
    public void DensitySingularityAtAnEndpointPropagatesBeforeSuccessfulEstimate()
    {
        var value = IntegrateSourceWindingSegment(Bits(0, 0), Bits(1, 0), Bits(0, 0), true, false, 1e-6, out var why);
        Assert.Null(why); Assert.NotNull(value); Assert.Equal(-1, value.Code); Assert.Equal(1, value.Evaluations); Assert.Null(value.ValueBits);
    }

    [Fact]
    public void InvalidCoordinateWidthIsNotZeroIntegral()
    {
        Assert.Null(IntegrateSourceWindingSegment([0], Bits(1, 0), Bits(0, 0), true, false, 1e-6, out var why));
        Assert.Equal("source-winding-segment", why);
    }

    static long[] Bits(params double[] v) => v.Select(BitConverter.DoubleToInt64Bits).ToArray();
}
