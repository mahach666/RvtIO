using static RvtIO.Parsing.TfBlockDeserializer;
using Xunit;

namespace RvtIO.Tests;

public class SourceWindingDensityTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void LiteralTauAndOrientationAreBitExact(bool orientation)
    {
        var value = Read(Bits(2, 3), Bits(6, 8), orientation);
        double denominator = (5.0 * 5 + 4.0 * 4) * BitConverter.Int64BitsToDouble(0x401921fb54442d11);
        Assert.Equal(Bits(denominator)[0], value.DenominatorBits); Assert.Equal(0, value.Code);
        long x = Bits(5.0 / denominator)[0] ^ long.MinValue, y = Bits(4.0 / denominator)[0];
        if (!orientation) { x ^= long.MinValue; y ^= long.MinValue; }
        Assert.Equal(new[] { x, y }, value.GradientBits);
        Assert.NotEqual(Bits((5.0 * 5 + 4.0 * 4) * Math.Tau)[0], value.DenominatorBits);
    }

    [Fact]
    public void SingularRadiusReturnsNativeMinusOneWithoutInventedGradient()
    {
        var value = Read(Bits(1, 2), Bits(1, 2), true);
        Assert.Equal(-1, value.Code); Assert.Equal(0L, value.DenominatorBits); Assert.Null(value.GradientBits);
    }

    [Fact]
    public void SingularityThresholdUsesScaledSquaredRadiusNotDistanceOrEps()
    {
        Assert.Equal(-1, Read(Bits(0, 0), Bits(1e-5, 0), true).Code);
        Assert.Equal(0, Read(Bits(0, 0), Bits(2e-5, 0), true).Code);
    }

    [Fact]
    public void SignedZeroUsesNativeXorNotARecomputedNegationConvention()
    {
        Assert.Equal(long.MinValue, Read(Bits(0, 0), Bits(1, 0), true).GradientBits![0]);
        Assert.Equal(0L, Read(Bits(0, 0), Bits(1, 0), false).GradientBits![0]);
    }

    [Theory]
    [InlineData("width")] [InlineData("nan")] [InlineData("overflow")]
    public void UnsupportedInputsFailExplicitly(string kind)
    {
        var q = Bits(0, 0); var p = Bits(1, 1);
        if (kind == "width") p = [0]; else if (kind == "nan") p = Bits(double.NaN, 1); else p = Bits(double.MaxValue, 0);
        Assert.Null(EvaluateSourceWindingDensity(q, p, true, out var why)); Assert.Equal("source-winding-density", why);
    }

    static SourceWindingDensity Read(long[] q, long[] p, bool orientation)
    { var value = EvaluateSourceWindingDensity(q, p, orientation, out var why); Assert.Null(why); Assert.NotNull(value); return value; }
    static long[] Bits(params double[] values) => values.Select(BitConverter.DoubleToInt64Bits).ToArray();
}
