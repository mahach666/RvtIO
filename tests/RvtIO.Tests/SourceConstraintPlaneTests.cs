using static RvtIO.Parsing.TfBlockDeserializer;
using Xunit;

namespace RvtIO.Tests;

public class SourceConstraintPlaneTests
{
    [Theory]
    [InlineData(1, 0, 0)] [InlineData(0, 1, 0)] [InlineData(0, 0, 1)]
    [InlineData(-1, 0, 0)] [InlineData(1, 2, 3)] [InlineData(1, 1e-12, 1e-12)]
    public void FiniteNonzeroDirectionsBuildPlaneAndPassCoincidentEarlyGate(double x, double y, double z)
    {
        var plane = PrepareSourceConstraintPlane(Vector(x, y, z), Vector(3, 4, 5), Vector(3, 4, 5), out var why);
        Assert.Null(why); Assert.NotNull(plane); Assert.True(plane.EarlyGateBothVersions);
        Assert.Equal(Vector(3, 4, 5), plane.ZeroUvPointBits);
        Assert.Equal(new long[] { 0, 0, 0 }, plane.DistanceBits);
    }

    [Fact]
    public void ParallelXAxisSelectionProducesPositiveYAxis()
    {
        var output = SelectSourceOrientedOrthogonalVector(Vector(1, 0, 0), out var why);
        Assert.Null(why); Assert.Equal(Vector(0, 1, 0), output);
    }

    [Fact]
    public void SeparatedPointsDoNotBecomeEarlySuccess()
    {
        var plane = PrepareSourceConstraintPlane(Vector(1, 0, 0), Vector(0, 0, 0), Vector(0, 0, 1), out _);
        Assert.NotNull(plane); Assert.False(plane.EarlyGateBothVersions);
    }

    [Fact]
    public void GateAtLegacyEpsilonUsesInclusiveComparison()
    {
        double eps = BitConverter.Int64BitsToDouble(0x3f75555555555555) / 10.0;
        var accepted = PrepareSourceConstraintPlane(Vector(1, 0, 0), Vector(0, 0, 0), Vector(eps, 0, 0), out _);
        var rejected = PrepareSourceConstraintPlane(Vector(1, 0, 0), Vector(0, 0, 0), Vector(Math.BitIncrement(eps), 0, 0), out _);
        Assert.NotNull(accepted); Assert.NotNull(rejected);
        Assert.True(accepted.EarlyGateBothVersions); Assert.False(rejected.EarlyGateBothVersions);
    }

    [Theory]
    [InlineData(0)] [InlineData(1e-10)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(1e200)]
    public void InvalidTangentNeverProducesSuccessfulConstraint(double x)
    {
        Assert.Null(PrepareSourceConstraintPlane(Vector(x, 0, 0), Vector(0, 0, 0), Vector(0, 0, 0), out var why));
        Assert.Equal("source-constraint-plane", why);
    }

    [Fact]
    public void BadSizesAndNonfinitePointsAreRejected()
    {
        Assert.Null(PrepareSourceConstraintPlane([0, 0], Vector(0, 0, 0), Vector(0, 0, 0), out _));
        Assert.Null(PrepareSourceConstraintPlane(Vector(1, 0, 0), [0, 0], Vector(0, 0, 0), out _));
        Assert.Null(PrepareSourceConstraintPlane(Vector(1, 0, 0), Vector(double.NaN, 0, 0), Vector(0, 0, 0), out _));
    }

    static long[] Vector(double x, double y, double z) =>
        [BitConverter.DoubleToInt64Bits(x), BitConverter.DoubleToInt64Bits(y), BitConverter.DoubleToInt64Bits(z)];
}
