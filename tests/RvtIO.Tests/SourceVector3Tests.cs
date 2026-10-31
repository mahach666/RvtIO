using static RvtIO.Parsing.TfBlockDeserializer;
using Xunit;

namespace RvtIO.Tests;

public class SourceVector3Tests
{
    [Theory]
    [InlineData(0, 0, 0, 0)] [InlineData(3, 4, 12, 13)] [InlineData(-3, -4, -12, 13)]
    public void LengthUsesOriginalThreeComponentSquare(double x, double y, double z, double length)
    {
        Assert.Equal(Bits(length), EvaluateSourceVectorLength(Vector(x, y, z), out var why)); Assert.Null(why);
    }

    [Fact]
    public void OverflowIsUnsupportedNotRescaledSuccess()
    {
        Assert.Null(EvaluateSourceVectorLength(Vector(1e200, 0, 0), out var why));
        Assert.Equal("source-vector-length", why);
    }

    [Fact]
    public void UnitEqualityAtTinyThresholdIsAccepted()
    {
        var value = EvaluateSourceVectorUnit(Vector(1e-9, -0.0, 0), out var why);
        Assert.Null(why); Assert.NotNull(value); Assert.Equal(0, value.Code);
        Assert.Equal(Vector(1, -0.0, 0), value.UnitBits);
    }

    [Fact]
    public void UnitAdjacentBelowThresholdReturnsNativeErrorAndPositiveZeros()
    {
        var value = EvaluateSourceVectorUnit(Vector(Math.BitDecrement(1e-9), 0, 0), out var why);
        Assert.Null(why); Assert.NotNull(value); Assert.Equal(4, value.Code);
        Assert.Equal(new long[] { 0, 0, 0 }, value.UnitBits);
    }

    [Fact]
    public void UnderflowRetainsNativeZeroLengthAndError()
    {
        var value = EvaluateSourceVectorUnit(Vector(1e-200, 0, 0), out _);
        Assert.NotNull(value); Assert.Equal(4, value.Code); Assert.Equal(0, value.LengthBits);
    }

    [Fact]
    public void UnitUsesDivisionNotRoundedReciprocalMultiplication()
    {
        var value = EvaluateSourceVectorUnit(Vector(1, 2, 3), out _)!;
        double length = Math.Sqrt(14);
        Assert.Equal(Vector(1 / length, 2 / length, 3 / length), value.UnitBits);
    }

    [Fact]
    public void DistanceSubtractsBeforeUnscaledLength()
    {
        Assert.Equal(Bits(13), EvaluateSourceVectorDistance(Vector(4, 6, 15), Vector(1, 2, 3), out var why));
        Assert.Null(why);
    }

    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    public void NonfiniteVectorDoesNotBecomeNativeUnitError(double invalid)
    {
        Assert.Null(EvaluateSourceVectorUnit(Vector(invalid, 0, 0), out var why)); Assert.NotNull(why);
    }

    [Fact]
    public void InvalidVectorSizesAreRejected()
    {
        Assert.Null(EvaluateSourceVectorLength([0, 0], out _));
        Assert.Null(EvaluateSourceVectorDistance([0, 0], [0, 0, 0], out _));
    }

    static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
    static long[] Vector(double x, double y, double z) => [Bits(x), Bits(y), Bits(z)];
}
