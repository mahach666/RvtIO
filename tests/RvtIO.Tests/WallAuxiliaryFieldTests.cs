using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class WallAuxiliaryFieldTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)]
    public void PatternMapsReadOrderedIntTransformAndIntElementIndexPairs(int count)
    {
        var payload = Bytes(w =>
        {
            w.Write((uint)count);
            for (int i = 0; i < count; i++) { w.Write(-100 - i); for (int d = 0; d < 12; d++) w.Write((double)(i * 17 + d)); }
            w.Write((uint)count);
            for (int i = 0; i < count; i++) { w.Write(10 + i); w.Write(-1); w.Write(-3 - i); }
        });
        Complete(496, payload);
        Assert.All(TfBlockDeserializer.SerializationFields(496)!, f => Assert.Null(f.Ref));
    }

    [Fact]
    public void NullCountSentinelsDoNotBecomeHugeInlineCollections()
        => Complete(496, Bytes(w => { w.Write(uint.MaxValue); w.Write(uint.MaxValue); }));

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)]
    public void BigTableReadsPersistedGeneratorIdsNotNativeMemoryNodePointers(int count)
    {
        Complete(532, Bytes(w =>
        {
            w.Write(0); w.Write(32); w.Write((uint)count);
            for (int i = 0; i < count; i++) w.Write(-1 + i);
            w.Write(0u); // no filled-table pointer slots
        }));
        Assert.Null(TfBlockDeserializer.SerializationFields(532)!.Single(f => f.Name == "m_bigTable").Ref);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)]
    public void StableCoverPairsReadGeomRefAndTwoHostIdentifiersWithoutRecursingIntoPair(int count)
    {
        Complete(877, Bytes(w =>
        {
            w.Write(0u); w.Write(0u); w.Write((uint)count);
            for (int i = 0; i < count; i++) CoverPair(w);
            w.Write((byte)1); w.Write((byte)0);
        }));
        Assert.Equal(878, TfBlockDeserializer.SerializationFields(879)!.Single(f => f.Name == "m_setting").Ref);
        Assert.Null(TfBlockDeserializer.SerializationFields(878)!.Single(f => f.Name == "second").Ref);
    }

    [Fact]
    public void StandaloneCoverPairAndHostInfoUseTheSameConcreteContracts()
    {
        Complete(878, Bytes(CoverPair));
        Complete(879, Bytes(w => { w.Write(-1); w.Write(int.MinValue); }));
    }

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(int.MinValue)] [InlineData(int.MaxValue)]
    public void ElementIdentifierBitsAreValuesNotPolymorphicMarkers(int id)
    {
        Complete(20, BitConverter.GetBytes(id));
        Assert.Null(TfBlockDeserializer.SerializationFields(20)!.Single().Ref);
    }

    [Theory]
    [InlineData(496)] [InlineData(532)] [InlineData(877)] [InlineData(878)] [InlineData(879)] [InlineData(20)]
    public void EveryTruncatedPayloadIsRejectedWithAnOtherwiseValidFrameFooter(int cls)
    {
        byte[] payload = cls switch
        {
            496 => Bytes(w => { w.Write(1u); w.Write(9); for (int d = 0; d < 12; d++) w.Write((double)d); w.Write(0u); }),
            532 => Bytes(w => { w.Write(0); w.Write(32); w.Write(1u); w.Write(12); w.Write(0u); }),
            877 => Bytes(w => { w.Write(0u); w.Write(0u); w.Write(1u); CoverPair(w); w.Write((byte)1); w.Write((byte)0); }),
            878 => Bytes(CoverPair),
            879 => Bytes(w => { w.Write(-1); w.Write(-2); }),
            _ => BitConverter.GetBytes(-1)
        };
        for (int length = 0; length < payload.Length; length++) Assert.Null(Read(Block(cls, payload[..length])));
    }

    [Theory]
    [InlineData(496)] [InlineData(532)] [InlineData(877)]
    public void ExcessiveCountsCannotProduceACompletedRoot(int cls)
    {
        var payload = Bytes(w => { if (cls != 496) { w.Write(0u); w.Write(0u); } w.Write(1_000_001u); });
        Assert.Null(Read(Block(cls, payload)));
    }

    [Fact]
    public void NonzeroFrameStartAndBadFooterRemainDistinct()
    {
        var bytes = Block(879, Bytes(w => { w.Write(-1); w.Write(-2); }));
        var container = new byte[bytes.Length + 19]; bytes.CopyTo(container, 7);
        Assert.NotNull(TfBlockDeserializer.ExtractParams(container, 7, BitConverter.ToUInt32(bytes, 8), 879));
        bytes[^1] ^= 1; Assert.Null(Read(bytes));
    }

    private static void CoverPair(BinaryWriter w)
    {
        w.Write(0.125); w.Write(5); // CoverFaceStableRef: distance, type, inline GeomRef
        w.Write(0u); w.Write(0u); // intermediate tags, null owned next ref
        w.Write(-1); w.Write(-1); w.Write(17); w.Write(-2); w.Write(0); w.Write(-1); w.Write((byte)0);
        w.Write(-1); w.Write(-2); // HostFaceInfo: two ElementId/Identifier values
    }
    private static void Complete(int cls, byte[] payload)
    {
        var bytes = Block(cls, payload); var result = Read(bytes);
        Assert.NotNull(result); Assert.True(result.RootParameterStoresComplete);
        Assert.Empty(TfBlockDeserializer.TraceQueue(bytes, 0, BitConverter.ToUInt32(bytes, 8), (ushort)cls));
    }
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
