using static RvtIO.Parsing.TfBlockDeserializer;
using Xunit;

namespace RvtIO.Tests;

public class SourceRefinedWindingIntegralTests
{
    [Fact]
    public void EveryIntegratorSampleChecksDynamicPlaneAndBothEpsGates()
    {
        var input = Input(); var refined = IntegrateSourceRefinedWindingSegment(input, 1e-6, out var why);
        var ordinary = IntegrateSourceWindingSegment(input.FirstBySideBits[0], input.LastBySideBits[0], input.QueryBits, true, false, 1e-6, out _);
        Assert.Null(why); Assert.NotNull(refined); Assert.Equal(ordinary, refined.Integral);
        Assert.Equal(refined.Integral.Evaluations, refined.ConstraintPreparations);
        Assert.Equal(refined.ConstraintPreparations, refined.EarlyGatePasses); Assert.True(refined.EveryVisitedRefinedPointResolved);
    }

    [Fact]
    public void DerivativeFlipDoesNotReverseRefinedParameter()
    {
        var positive = IntegrateSourceRefinedWindingSegment(Input(), 1e-6, out _)!;
        var negative = IntegrateSourceRefinedWindingSegment(Input() with { EdgeFlags = 7 }, 1e-6, out _)!;
        Assert.Equal(0, negative.Integral.Code);
        Assert.Equal(positive.Integral.ValueBits!.Value ^ long.MinValue, negative.Integral.ValueBits);
        Assert.Equal(positive.Integral.Evaluations, negative.Integral.Evaluations);
    }

    [Theory]
    [InlineData(0, 6, false)] [InlineData(0, 7, true)] [InlineData(1, 6, true)] [InlineData(1, 7, false)]
    public void SideAndFlagDetermineSeparateDerivativeSign(int side, int flags, bool negative)
    {
        var baseline = IntegrateSourceRefinedWindingSegment(Input(), 1e-6, out _)!;
        var actual = IntegrateSourceRefinedWindingSegment(Input() with { FaceSide = side, EdgeFlags = flags }, 1e-6, out _)!;
        Assert.Equal(baseline.Integral.ValueBits!.Value ^ (negative ? long.MinValue : 0), actual.Integral.ValueBits);
    }

    [Fact]
    public void UnclosedIntersectionDoesNotProduceOutsideOrSuccessfulIntegral()
    {
        var input = Input(); var planes = input.Planes.ToArray(); planes[1] = planes[1] with { OriginBits = V3(0, 0, 1) };
        Assert.Null(IntegrateSourceRefinedWindingSegment(input with { Planes = planes }, 1e-6, out var why));
        Assert.Equal("source-refined-intersection-pending", why);
    }

    [Fact]
    public void DegeneratePreparationIsExplicitUnsupported()
    {
        var input = Input(); var planes = input.Planes.Select(p => p with { XBits = V3(0, 0, 0) }).ToArray();
        Assert.Null(IntegrateSourceRefinedWindingSegment(input with { Planes = planes }, 1e-6, out var why));
        Assert.Equal("source-refined-constraint", why);
    }

    [Fact]
    public void DensityErrorPropagatesAfterResolvedPoint()
    {
        var value = IntegrateSourceRefinedWindingSegment(Input() with { QueryBits = V2(0, 0) }, 1e-6, out var why);
        Assert.Null(why); Assert.NotNull(value); Assert.Equal(-1, value.Integral.Code);
        Assert.Equal(1, value.ConstraintPreparations); Assert.Equal(1, value.EarlyGatePasses);
    }

    [Fact]
    public void NonpositiveEpsilonDoesNotInvokeRefinement()
    {
        var value = IntegrateSourceRefinedWindingSegment(Input(), 0, out var why);
        Assert.Null(why); Assert.NotNull(value); Assert.Equal(-1, value.Integral.Code); Assert.Equal(0, value.ConstraintPreparations);
    }

    [Theory]
    [InlineData(-1, 6)] [InlineData(2, 6)] [InlineData(0, 2)] [InlineData(0, 256)]
    public void InvalidSideFlagsAndPointCountAreRejected(int side, int flags)
    {
        Assert.Null(IntegrateSourceRefinedWindingSegment(Input() with { FaceSide = side, EdgeFlags = flags }, 1e-6, out var why));
        Assert.Equal("source-refined-winding-input", why);
    }

    [Fact]
    public void NonfiniteInputIsNotNativeSuccessfulErrorFallback()
    {
        Assert.Null(IntegrateSourceRefinedWindingSegment(Input() with { QueryBits = V2(double.NaN, 0) }, 1e-6, out var why));
        Assert.Equal("source-refined-winding-input", why);
    }

    static SourceRefinedWindingInput Input()
    {
        SourceWindingPlane plane = new(V3(0, 0, 0), V3(1, 0, 0), V3(0, 1, 0));
        return new([V2(0, 0), V2(0, 0)], [V2(1, 0), V2(1, 0)], [plane, plane], 0, 6, V2(0, 1), true);
    }
    static long[] V2(double x, double y) => [BitConverter.DoubleToInt64Bits(x), BitConverter.DoubleToInt64Bits(y)];
    static long[] V3(double x, double y, double z) => [BitConverter.DoubleToInt64Bits(x), BitConverter.DoubleToInt64Bits(y), BitConverter.DoubleToInt64Bits(z)];
}
