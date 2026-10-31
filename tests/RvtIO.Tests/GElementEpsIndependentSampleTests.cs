using static RvtIO.Parsing.TfBlockDeserializer;
using Xunit;

namespace RvtIO.Tests;

public class GElementEpsIndependentSampleTests
{
    public static IEnumerable<object?[]> Decisions()
    {
        bool?[] values = [null, false, true];
        foreach (var a in values) foreach (var b in values) yield return [a, b];
    }

    [Theory]
    [MemberData(nameof(Decisions))]
    public void OnlyKnownEqualBranchDecisionsAreIndependent(bool? a, bool? b)
    {
        Assert.Equal(a != null && b != null && a == b ? a : null, CompareSourceEpsDecisions(a, b));
    }

    [Fact]
    public void EmptySameGraphKeepsBothBranchesAndDoesNotInferRuntimeVersion()
    {
        OwnGElementGeometryGraph graph = new(100, 0, [new(-1, 2007, 100, 0, 8, [])], [], 8);
        var result = SelectOwnGElementEpsIndependentSamples(graph, out var why);
        Assert.Null(why); Assert.NotNull(result);
        Assert.Equal(0, result.Legacy.FirstSample.CurrentTessEpsVersion); Assert.Equal(1, result.Positive.FirstSample.CurrentTessEpsVersion);
        Assert.False(result.ActualRuntimeVersionInferred); Assert.False(result.CurrentGeometryAuthorityClosed);
    }

    [Fact]
    public void EnvelopeSensitivityControlDetectsNativeEpsilonBranchDifference()
    {
        double a = Eps(0), b = Eps(1), middle = .5*a+.5*b;
        var legacy = EvaluateSourceEnvelopeContainment(V(0, 0, 1, 1), V(1+middle, .5), Bits(a), V(1, 0, 0, 1), out _);
        var positive = EvaluateSourceEnvelopeContainment(V(0, 0, 1, 1), V(1+middle, .5), Bits(b), V(1, 0, 0, 1), out _);
        Assert.NotNull(legacy); Assert.NotNull(positive); Assert.False(legacy.IsInside); Assert.True(positive.IsInside);
        Assert.Null(CompareSourceEpsDecisions(legacy.IsInside, positive.IsInside));
    }

    [Fact]
    public void ProjectionBoundarySensitivityIsNotHiddenByParityFold()
    {
        double a = Eps(0), b = Eps(1);var p=ProjectSourceTwoPointSegment(V(0,0,0),V(1,0,0),V(.5,.5*a+.5*b,0),out _)!;
        double distance=BitConverter.Int64BitsToDouble(p.DistanceBits);
        Assert.False(a>=distance);Assert.True(b>=distance);Assert.Null(CompareSourceEpsDecisions(a>=distance,b>=distance));
    }

    static long Bits(double value)=>BitConverter.DoubleToInt64Bits(value);
    static long[] V(params double[] values)=>values.Select(Bits).ToArray();
    static double Eps(int version)=>BitConverter.Int64BitsToDouble(SelectSourceTessEpsControl(0,version,null,out _)!.VertexEpsilonBits);
}
