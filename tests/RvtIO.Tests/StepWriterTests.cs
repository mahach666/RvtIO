using RvtIO.Ifc;
using Xunit;

namespace RvtIO.Tests;

public class StepWriterTests
{
    [Theory]
    [InlineData(-1e-12, "-1.0E-12")]
    [InlineData(1e20, "1.0E+20")]
    [InlineData(1.25e-12, "1.25E-12")]
    [InlineData(2, "2.")]
    [InlineData(2.5, "2.5")]
    public void Real_PutsDecimalPointInExponentMantissa(double value, string expected)
    {
        Assert.Equal(expected, StepWriter.Real(value));
    }
}
