using Xunit;

namespace RvtIO.Tests;

public class RootProbeStairBoundaryTests
{
    [Fact]
    public void HypothesisPreservesNativeValuesAndStopsBeforeNextField()
    {
        byte[] bytes = Maps();
        Assert.True(StairBoundaryProbe.TryCompatibleCurveInfo(bytes, 0, bytes.Length, out int end, out var maps));
        Assert.Equal(bytes.Length - 4, end);
        Assert.Equal(new[] { new StairBoundaryProbe.Entry(-0.25, -1, int.MinValue), new(0.5, 3, 4) }, maps[0]);
        Assert.Empty(maps[1]);
    }

    [Theory]
    [InlineData(0u)] [InlineData(uint.MaxValue)]
    public void EmptyHypothesisUsesOneCountOnly(uint count)
    {
        byte[] bytes = BitConverter.GetBytes(count).Concat(BitConverter.GetBytes(123u)).ToArray();
        Assert.True(StairBoundaryProbe.TryCompatibleCurveInfo(bytes, 0, bytes.Length, out int end, out var maps));
        Assert.Equal(4, end); Assert.Empty(maps);
    }

    [Fact]
    public void EveryTruncatedPayloadIsRejectedWithoutPartialMaps()
    {
        byte[] bytes = Maps();
        for (int limit = 0; limit < bytes.Length - 4; limit++)
        {
            Assert.False(StairBoundaryProbe.TryCompatibleCurveInfo(bytes, 0, limit, out int end, out var maps));
            Assert.Equal(-1, end); Assert.Empty(maps);
        }
    }

    [Theory]
    [InlineData("outer_count")] [InlineData("inner_count")]
    [InlineData("nan")] [InlineData("infinity")]
    [InlineData("negative_start")] [InlineData("overflow_start")] [InlineData("overflow_end")]
    public void MalformedAndInvalidBoundsFailClosed(string mode)
    {
        byte[] bytes = Maps();
        int start = mode == "negative_start" ? -1 : mode == "overflow_start" ? int.MaxValue : 0;
        int limit = mode == "overflow_end" ? int.MaxValue : bytes.Length;
        if (mode.EndsWith("count")) BitConverter.GetBytes(100001u).CopyTo(bytes, mode == "outer_count" ? 0 : 4);
        if (mode is "nan" or "infinity") BitConverter.GetBytes(mode == "nan" ? double.NaN : double.PositiveInfinity).CopyTo(bytes, 8);
        Assert.False(StairBoundaryProbe.TryCompatibleCurveInfo(bytes, start, limit, out int end, out var maps));
        Assert.Equal(-1, end); Assert.Empty(maps);
    }

    private static byte[] Maps()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(2u); writer.Write(2u);
        writer.Write(-0.25); writer.Write(-1); writer.Write(int.MinValue);
        writer.Write(0.5); writer.Write(3); writer.Write(4);
        writer.Write(uint.MaxValue); writer.Write(123u);
        return stream.ToArray();
    }
}
