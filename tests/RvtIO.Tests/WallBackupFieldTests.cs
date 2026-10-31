using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class WallBackupFieldTests
{
    [Theory]
    [InlineData(1249, 0)] [InlineData(1249, 1)] [InlineData(1249, 3)]
    [InlineData(1591, 0)] [InlineData(1591, 1)] [InlineData(1591, 3)]
    public void CountedBackupsReadInlinePairsInOrder(int cls, int count)
    {
        Complete(cls, Bytes(w =>
        {
            w.Write((uint)count);
            for (int i = 0; i < count; i++)
            {
                w.Write(-1 - i);
                if (cls == 1249) Edge(w, i); else Face(w);
            }
        }));
        Assert.Null(TfBlockDeserializer.SerializationFields(cls)!.Single().Ref);
        Assert.Null(TfBlockDeserializer.SerializationFields(cls == 1249 ? 1250 : 1592)![1].Ref);
    }

    [Theory]
    [InlineData(1249)] [InlineData(1591)]
    public void NullMapSentinelIsNotAnObjectOrAnEnormousCount(int cls)
        => Complete(cls, BitConverter.GetBytes(uint.MaxValue));

    [Theory]
    [InlineData(1250, -1)] [InlineData(1250, 0)] [InlineData(1250, int.MinValue)] [InlineData(1250, int.MaxValue)]
    [InlineData(1592, -1)] [InlineData(1592, 0)] [InlineData(1592, int.MinValue)] [InlineData(1592, int.MaxValue)]
    public void StandalonePairsKeepKeyBitsAsIntegers(int cls, int key)
        => Complete(cls, Bytes(w => { w.Write(key); if (cls == 1250) Edge(w, 1); else Face(w); }));

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)]
    public void EdgeValuesReadInteriorAndTwoFixedUvPairs(int count)
        => Complete(1251, Bytes(w => Edge(w, count)));

    [Fact]
    public void EdgeInteriorNullSentinelStillReadsBothFixedPointsAndFlags()
        => Complete(1251, Bytes(w => { w.Write(uint.MaxValue); Point(w, 0); Point(w, 1); w.Write((byte)0xA5); }));

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(255)]
    public void FaceOrientationByteDoesNotChooseAByteBoundary(int orientation)
        => Complete(1593, Bytes(w => { w.Write((byte)orientation); w.Write(0u); }));

    [Fact]
    public void NonzeroExternalSurfaceReferenceHasNoDeferredPayload()
        => Complete(1591, Bytes(w => { w.Write(1u); w.Write(-1); w.Write((byte)1); w.Write(73u); w.Write((ushort)0); }));

    [Theory]
    [InlineData(73u)] [InlineData(uint.MaxValue)]
    public void OwnedSurfacePointersRetainQueueIdentityWithoutClaimingCompleteGeometry(uint handle)
    {
        var bytes = Block(1591, Bytes(w =>
        {
            w.Write(2u);
            for (int i = 0; i < 2; i++) { w.Write(-1 - i); w.Write((byte)1); w.Write(handle); w.Write((ushort)522); }
        }));
        var queue = TfBlockDeserializer.TraceQueue(bytes, 0, BitConverter.ToUInt32(bytes, 8), 1591);
        Assert.Equal(2, queue.Count);
        Assert.All(queue, entry => Assert.Equal(((ushort)522, handle), entry));
        var trace = Trace(bytes);
        Assert.Null(trace.Failure); Assert.Equal(bytes.Length - 4, trace.End);
        Assert.Equal(queue, trace.Queued);
        // ExtractParams promises the root parameter prefix, not a complete geometry tail.
        Assert.NotNull(Read(bytes));
    }

    [Theory]
    [InlineData(1249)] [InlineData(1591)] [InlineData(1250)]
    [InlineData(1592)] [InlineData(1251)] [InlineData(1593)]
    public void EveryTruncationFailsEvenWithAReframedValidFooter(int cls)
    {
        byte[] payload = Payload(cls);
        for (int length = 0; length < payload.Length; length++)
            Assert.Null(Read(Block(cls, payload[..length])));
    }

    [Theory]
    [InlineData(1249)] [InlineData(1591)]
    public void ExcessiveMapCountIsRejected(int cls)
        => Assert.Null(Read(Block(cls, BitConverter.GetBytes(1_000_001u))));

    [Fact]
    public void ExcessiveInteriorCountIsRejectedAndTrailingBytesAreNotConsumedAsMapValues()
    {
        Assert.Null(Read(Block(1251, BitConverter.GetBytes(1_000_001u))));
        var bytes = Block(1249, [0, 0, 0, 0, 42]);
        var trace = Trace(bytes);
        Assert.Null(trace.Failure); Assert.Equal(18, trace.End);
        Assert.NotNull(Read(bytes)); // legacy root-prefix API does not reject unrelated tail bytes
    }

    [Fact]
    public void NonzeroFrameStartAndBadFooterRemainChecked()
    {
        var bytes = Block(1249, Payload(1249));
        var container = new byte[bytes.Length + 19]; bytes.CopyTo(container, 7);
        Assert.NotNull(TfBlockDeserializer.ExtractParams(container, 7, BitConverter.ToUInt32(bytes, 8), 1249));
        bytes[^1] ^= 1; Assert.Null(Read(bytes));
    }

    private static byte[] Payload(int cls) => Bytes(w =>
    {
        if (cls is 1249 or 1591) w.Write(1u);
        if (cls is 1249 or 1591 or 1250 or 1592) w.Write(-1);
        if (cls is 1249 or 1250 or 1251) Edge(w, 1); else Face(w);
    });
    private static void Face(BinaryWriter w) { w.Write((byte)1); w.Write(0u); }
    private static void Edge(BinaryWriter w, int count)
    {
        w.Write((uint)count);
        for (int i = 0; i < count; i++) Point(w, i + 2);
        Point(w, 0); Point(w, 1); w.Write((byte)0xA5);
    }
    private static void Point(BinaryWriter w, int index)
    {
        // EdgePnt.uv has two inline UV values, each two f64 coordinates.
        for (int coordinate = 0; coordinate < 4; coordinate++) w.Write(index * 11.0 + coordinate + 0.125);
    }
    private static void Complete(int cls, byte[] payload)
    {
        var bytes = Block(cls, payload); var result = Read(bytes);
        Assert.NotNull(result); Assert.True(result.RootParameterStoresComplete);
        Assert.Empty(TfBlockDeserializer.TraceQueue(bytes, 0, BitConverter.ToUInt32(bytes, 8), (ushort)cls));
        var trace = Trace(bytes);
        Assert.Null(trace.Failure); Assert.Equal(bytes.Length - 4, trace.End);
        Assert.Empty(trace.Queued);
    }
    private static DeferredFieldProbe.Result Trace(byte[] bytes)
        => DeferredFieldProbe.Read(bytes, 14, bytes.Length - 4,
            BitConverter.ToUInt16(bytes, 12), BitConverter.ToUInt16(bytes, 12));
    private static TfBlockDeserializer.NativeParams? Read(byte[] bytes)
        => TfBlockDeserializer.ExtractParams(bytes, 0, BitConverter.ToUInt32(bytes, 8), BitConverter.ToUInt16(bytes, 12));
    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        write(writer); return stream.ToArray();
    }
    private static byte[] Block(int cls, byte[] payload) => Bytes(w =>
    {
        uint size = (uint)payload.Length + 2;
        w.Write(100); w.Write(0u); w.Write(size); w.Write((ushort)cls); w.Write(payload); w.Write(size);
    });
}
