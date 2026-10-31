using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class WallExtrusionHelperTests
{
    [Theory]
    [InlineData(3524)] [InlineData(361)]
    public void WallWithoutAHelperCellIsReadableAndHasNone(int root)
    {
        var input = Read(Frame((ushort)root, 2075, Bytes(w => { w.Write(-1); w.Write(int.MinValue); })))!;
        Assert.Equal((ushort)root, input.RootClassId);
        Assert.False(input.Present);
        Assert.False(input.Complete);
        Assert.Empty(input.LoopCurveCounts);
    }

    [Theory]
    [InlineData(new[] { 4 }, true)]
    [InlineData(new[] { 8 }, true)]
    [InlineData(new[] { 4, 6 }, true)]
    [InlineData(new[] { 5 }, false)]
    [InlineData(new int[0], true)]
    public void HelperLoopsAreCountedByTheirCurves(int[] curves, bool complete)
    {
        var input = Read(Frame(3524, 4041, HelperPayload(complete, curves)))!;
        Assert.True(input.Present);
        Assert.Equal(complete, input.Complete);
        Assert.Equal(curves, input.LoopCurveCounts);
    }

    [Fact]
    public void UnreadableFrameIsNotReportedAsAWallWithoutAHelper()
    {
        byte[] payload = HelperPayload(true, [4]);
        Assert.Null(Read(Frame(3524, 4041, payload[..^1])));
        Assert.Null(Read(Frame(3524, 4041, [.. payload, 0])));
        byte[] bytes = Frame(3524, 4041, payload);
        bytes[^1] ^= 1;
        Assert.Null(Read(bytes));
    }

    [Fact]
    public void LoopPointerOfAnotherClassIsRejected()
    {
        byte[] payload = HelperPayload(true, [4]);
        // The first pointer of m_pCurveLoops follows m_sketchId (4), m_complete (1) and the count (4).
        BitConverter.GetBytes((ushort)1736).CopyTo(payload, 4 + 1 + 4 + 4);
        Assert.Null(Read(Frame(3524, 4041, payload)));
    }

    private static TfBlockDeserializer.OwnWallHelperInput? Read(byte[] bytes)
        => TfBlockDeserializer.ExtractOwnWallHelperInput(bytes, 0, BitConverter.ToUInt32(bytes, 8), BitConverter.ToUInt16(bytes, 12));

    /// <summary>ExtrusionHelper.m_sketchId, m_complete, ElementExtrusionHelper.m_pCurveLoops, then (queue order) the
    /// CurveLoop objects (m_open, m_curves) and the GLine objects of all loops.</summary>
    private static byte[] HelperPayload(bool complete, int[] curvesPerLoop) => Bytes(w =>
    {
        uint handle = 100;
        w.Write(-1);
        w.Write(complete);
        w.Write((uint)curvesPerLoop.Length);
        foreach (int _ in curvesPerLoop) { w.Write(handle++); w.Write((ushort)938); }
        foreach (int count in curvesPerLoop)
        {
            w.Write(false);
            w.Write((uint)count);
            for (int i = 0; i < count; i++) { w.Write(handle++); w.Write((ushort)1736); }
        }
        foreach (int count in curvesPerLoop) w.Write(new byte[80 * count]);
    });

    /// <summary>A wall root whose complete owned FIFO holds one object of class <paramref name="firstClass"/> with the payload.</summary>
    private static byte[] Frame(ushort root, ushort firstClass, byte[] payload)
    {
        var (original, offsets) = WallGeometryPhaseTests.Block(root,
            geometryTablePointer: Bytes(w => { w.Write(uint.MaxValue); w.Write(firstClass); }));
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(original.AsSpan(0, offsets["list-end"]));
        writer.Write(payload);
        writer.Write(0u);
        byte[] bytes = stream.ToArray();
        uint size = (uint)bytes.Length - 16;
        BitConverter.GetBytes(size).CopyTo(bytes, 8);
        BitConverter.GetBytes(size).CopyTo(bytes, bytes.Length - 4);
        return bytes;
    }

    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        write(writer);
        return stream.ToArray();
    }
}
