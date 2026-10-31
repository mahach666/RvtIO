using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class CurtainAuxiliaryFieldTests
{
    [Theory]
    [InlineData(90, 0)] [InlineData(90, 1)] [InlineData(90, 3)]
    [InlineData(370, 0)] [InlineData(370, 1)] [InlineData(370, 3)]
    [InlineData(586, 0)] [InlineData(586, 1)] [InlineData(586, 3)]
    public void CountedInlineValuesHaveExactSpansAndNoPointerQueue(int cls, int count)
        => Complete(cls, Payload(cls, count));

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)]
    public void CurtainDriverHasTwoRulesThenCountedJoinsAndItsOwnTail(int count)
    {
        Complete(368, Payload(368, count));
        Assert.Null(TfBlockDeserializer.SerializationFields(368)!.Single(f => f.Name == "m_ruleInst").Ref);
        Assert.Equal(2, TfBlockDeserializer.SerializationFields(368)!.Single(f => f.Name == "m_ruleInst").Cnt);
    }

    [Theory]
    [InlineData(90)] [InlineData(370)] [InlineData(586)]
    public void NullCollectionSentinelsAreNotHugeCounts(int cls)
        => Complete(cls, Bytes(w =>
        {
            w.Write(uint.MaxValue);
            if (cls == 370) { w.Write(-1); w.Write((byte)1); }
            if (cls == 586) w.Write(uint.MaxValue);
        }));

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(int.MinValue)] [InlineData(int.MaxValue)]
    public void StandaloneIdIndexSetPreservesIdentifierAndIndexBits(int value)
        => Complete(90, Bytes(w => { w.Write(2u); for (int i = 0; i < 2; i++) { w.Write(value); w.Write(value); } }));

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(int.MinValue)] [InlineData(int.MaxValue)]
    public void MullionInfoValuesDoNotQueueAnObjectForIdentifierSentinelBits(int value)
        => Complete(586, Bytes(w => { w.Write(1u); w.Write(value); w.Write(value); w.Write(1u); w.Write(value); w.Write(value); }));

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(255)]
    public void RuleAndGridFlagsDoNotSelectAByteBoundary(int flag)
    {
        Complete(369, Bytes(w => Rule(w, 7, (byte)flag)));
        Complete(370, Bytes(w => { w.Write(0u); w.Write(-1); w.Write((byte)flag); }));
    }

    [Fact]
    public void FixedRulesDoNotConsumeACountPrefixOrWriteTheSharedSchema()
    {
        var bytes = Block(368, Payload(368, 0)); var trace = Trace(bytes);
        var rules = Assert.Single(trace.Fields, f => f.Name == "m_ruleInst");
        Assert.Equal(98, rules.End - rules.Start);
        Assert.Null(TfBlockDeserializer.SerializationFields(368)!.Single(f => f.Name == "m_ruleInst").Ref);
        Assert.Null(TfBlockDeserializer.SerializationFields(370)![0].Ref);
        Assert.Null(TfBlockDeserializer.SerializationFields(586)![0].Ref);
        Assert.Null(TfBlockDeserializer.SerializationFields(90)![0].Ref);
    }

    [Theory]
    [InlineData(90)] [InlineData(368)] [InlineData(369)] [InlineData(370)]
    [InlineData(371)] [InlineData(586)] [InlineData(587)]
    public void EveryTruncationFailsWithAnOtherwiseValidFrameFooter(int cls)
    {
        var payload = Payload(cls, 1);
        for (int length = 0; length < payload.Length; length++)
        {
            var bytes = Block(cls, payload[..length]);
            Assert.Null(Read(bytes)); Assert.NotNull(Trace(bytes).Failure);
        }
    }

    [Theory]
    [InlineData(90)] [InlineData(370)] [InlineData(586)]
    public void ExcessiveCountsAreRejected(int cls)
        => Assert.Null(Read(Block(cls, BitConverter.GetBytes(1_000_001u))));

    [Fact]
    public void OwnedPointersRetainQueueOrderAndRootPrefixContract()
    {
        var payload = Payload(368, 0);
        var owned = Bytes(w => { w.Write(73u); w.Write((ushort)901); w.Write(payload[4..]); });
        var bytes = Block(368, owned); var trace = Trace(bytes);
        Assert.Null(trace.Failure); Assert.Equal(bytes.Length - 4, trace.End);
        Assert.Equal(new[] { ((ushort)901, 73u) }, trace.Queued);
        Assert.NotNull(Read(bytes)); // deferred grid geometry is outside ExtractParams's prefix contract
    }

    [Fact]
    public void NonzeroFrameStartAndInvalidFooterAreChecked()
    {
        var bytes = Block(90, Payload(90, 1)); var container = new byte[bytes.Length + 17]; bytes.CopyTo(container, 9);
        Assert.NotNull(TfBlockDeserializer.ExtractParams(container, 9, BitConverter.ToUInt32(bytes, 8), 90));
        bytes[^1] ^= 1; Assert.Null(Read(bytes));
    }

    private static byte[] Payload(int cls, int count) => Bytes(w =>
    {
        if (cls == 368)
        {
            w.Write(0u); w.Write(0u); w.Write(0u); Rule(w, 1, 1); Rule(w, 2, 0);
            w.Write((uint)count); for (int i = 0; i < count; i++) Join(w, i + 1);
            w.Write(0u); w.Write(0u); w.Write(-1);
        }
        else if (cls == 369) Rule(w, 1, 1);
        else if (cls == 370) Join(w, count);
        else if (cls is 371 or 587) { w.Write(-1); w.Write(int.MinValue); }
        else
        {
            w.Write((uint)count); for (int i = 0; i < count; i++) { w.Write(-1 - i); w.Write(int.MinValue + i); }
            if (cls == 586) { w.Write((uint)count); for (int i = 0; i < count; i++) { w.Write(-10 - i); w.Write(100 + i); } }
        }
    });
    private static void Rule(BinaryWriter w, int index, byte flag)
    {
        for (int i = 0; i < 5; i++) w.Write(index * 11.0 + i + 0.125);
        w.Write(-1); w.Write(int.MinValue); w.Write(flag);
    }
    private static void Join(BinaryWriter w, int count)
    {
        w.Write((uint)count); for (int i = 0; i < count; i++) { w.Write(-1 - i); w.Write(int.MinValue + i); }
        w.Write(-1); w.Write((byte)1);
    }
    private static void Complete(int cls, byte[] payload)
    {
        var bytes = Block(cls, payload); var result = Read(bytes);
        Assert.NotNull(result); Assert.True(result.RootParameterStoresComplete);
        var trace = Trace(bytes); Assert.Null(trace.Failure);
        Assert.Equal(bytes.Length - 4, trace.End); Assert.Empty(trace.Queued);
    }
    private static DeferredFieldProbe.Result Trace(byte[] bytes)
        => DeferredFieldProbe.Read(bytes, 14, bytes.Length - 4, BitConverter.ToUInt16(bytes, 12), BitConverter.ToUInt16(bytes, 12));
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
