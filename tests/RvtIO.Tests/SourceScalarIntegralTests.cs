using static RvtIO.Parsing.TfBlockDeserializer;
using Xunit;

namespace RvtIO.Tests;

public class SourceScalarIntegralTests
{
    [Theory]
    [InlineData(0, 1)] [InlineData(1, 0.5)] [InlineData(2, 0.3333333333333333)]
    [InlineData(3, 0.25)] [InlineData(4, 0.2)]
    public void PolynomialExtrapolationUsesFiveLevels(int power, double expected)
    {
        var value = Integrate(x => Math.Pow(x, power), 0, 1);
        Assert.Equal(0, value.Code); Assert.Equal(5, value.Levels); Assert.Equal(17, value.Evaluations);
        Assert.InRange(Math.Abs(Number(value.ValueBits!.Value) - expected), 0, 1e-15);
    }

    [Fact]
    public void ReversedIntervalRetainsSignAndNativeSampleOrder()
    {
        var visits = new List<double>();
        var value = IntegrateSourceScalar(x => { visits.Add(x); return (0, Bits(x)); }, 1, 0, 1e-6, out var why);
        Assert.Null(why); Assert.NotNull(value); Assert.Equal(Bits(-0.5), value.ValueBits);
        Assert.Equal(new double[] { 1, 0, 0.5, 0.75, 0.25 }, visits.Take(5));
    }

    [Fact]
    public void TinyResultShortcutDoesNotRequireRelativeErrorConvergence()
    {
        var value = Integrate(x => 1e-10 * Math.Exp(x), 0, 1, 1e-30);
        Assert.Equal(0, value.Code); Assert.Equal(5, value.Levels);
        Assert.True(Math.Abs(Number(value.ErrorBits!.Value)) > Math.Abs(Number(value.ValueBits!.Value)) * 1e-30);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(4)]
    public void CallbackErrorsPropagateAndStopWithoutInventedIntegral(int failedAt)
    {
        int count = 0;
        var value = IntegrateSourceScalar(x => (++count == failedAt ? 73 : 0, Bits(x)), 0, 1, 1e-6, out var why);
        Assert.Null(why); Assert.NotNull(value); Assert.Equal(73, value.Code);
        Assert.Equal(failedAt, value.Evaluations); Assert.Null(value.ValueBits); Assert.Null(value.ErrorBits);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)]
    public void NonpositiveEpsilonReturnsNativeMinusOneBeforeCallback(double epsilon)
    {
        var value = IntegrateSourceScalar(x => throw new InvalidOperationException(), 0, 1, epsilon, out var why);
        Assert.Null(why); Assert.NotNull(value); Assert.Equal(-1, value.Code); Assert.Equal(0, value.Evaluations);
    }

    [Fact]
    public void SmoothNonPolynomialRefinesAndKeepsResultAndErrorBits()
    {
        var value = Integrate(Math.Exp, 0, 1);
        Assert.Equal(0, value.Code); Assert.InRange(Number(value.ValueBits!.Value), Math.E - 1 - 1e-6, Math.E - 1 + 1e-6);
        Assert.NotNull(value.ErrorBits);
    }

    [Fact]
    public void ExhaustionReturnsNativeMinusOneWithLastEstimateNotSuccessfulIntegral()
    {
        int evaluations = 0;
        var value = IntegrateSourceScalar(x => (0, Bits((++evaluations & 1) == 1 ? 3 : 1)), 0, 1, 1e-40, out var why);
        Assert.Null(why); Assert.NotNull(value); Assert.Equal(-1, value.Code);
        Assert.Equal(20, value.Levels); Assert.Equal(524289, value.Evaluations);
        Assert.NotNull(value.ValueBits); Assert.NotNull(value.ErrorBits);
    }

    [Theory]
    [InlineData("callback")] [InlineData("bounds")] [InlineData("epsilon")] [InlineData("missing")]
    public void UnsupportedNonfiniteInputsRemainFailuresNotSuccess(string kind)
    {
        SourceScalarIntegrand? callback = x => (0, Bits(kind == "callback" ? double.NaN : x));
        if (kind == "missing") callback = null;
        var value = IntegrateSourceScalar(callback, kind == "bounds" ? double.PositiveInfinity : 0, 1,
            kind == "epsilon" ? double.NaN : 1e-6, out var why);
        Assert.Null(value); Assert.Equal("source-scalar-integral", why);
    }

    static SourceScalarIntegral Integrate(Func<double, double> f, double a, double b, double epsilon = 1e-6)
    {
        var value = IntegrateSourceScalar(x => (0, Bits(f(x))), a, b, epsilon, out var why);
        Assert.Null(why); Assert.NotNull(value); return value;
    }
    static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
    static double Number(long bits) => BitConverter.Int64BitsToDouble(bits);
}
