using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class ElemRecPointersInlineTests
{
    [Theory]
    [InlineData(int.MinValue)] [InlineData(-1)] [InlineData(0)]
    [InlineData(1)] [InlineData(int.MaxValue)]
    public void BareSignedIdAndBothNullPointersAreReadAsTheInlineRecord(int id)
    {
        var block = Block(Payload(id, 0, 0, 0, 0));
        var result = Read(block);
        Assert.NotNull(result); Assert.True(result.RootParameterStoresComplete);
        Assert.Empty(Queue(block));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void InlineRecordQueuesElementThenGrepInSourceFieldOrder(bool element, bool grep)
    {
        var block = Block(Payload(717, element ? 40u : 0u, 37, grep ? 50u : 0u, 2007));
        Assert.NotNull(Read(block));
        var expected = new List<(ushort, uint)>();
        if (element) expected.Add((37, 40));
        if (grep) expected.Add((2007, 50));
        Assert.Equal(expected, Queue(block));
        // This checks the root's pointer queue. Deferred Element/GRep payloads
        // are deliberately outside this fixture; it is not a complete geometry.
    }

    [Fact]
    public void ExternalTagsOwnNoDeferredPayloadAndDoNotHideTheNextPointer()
    {
        var block = Block(Payload(123, uint.MaxValue, 0, 77, 2007));
        Assert.NotNull(Read(block)); Assert.Equal(new[] { ((ushort)2007, 77u) }, Queue(block));
        block = Block(Payload(123, 33, 37, uint.MaxValue, 0));
        Assert.NotNull(Read(block)); Assert.Equal(new[] { ((ushort)37, 33u) }, Queue(block));
    }

    [Fact]
    public void EqualOwnedHandlesDoNotMergeTheTwoPointers()
    {
        var block = Block(Payload(55, 99, 37, 99, 2007));
        Assert.Equal(new[] { ((ushort)37, 99u), ((ushort)2007, 99u) }, Queue(block));
    }

    [Fact]
    public void EveryTruncatedInlineByteIsRejectedWithAnOtherwiseValidFooter()
    {
        var payload = Payload(717, 40, 37, 50, 2007);
        for (int count = 0; count < payload.Length; count++)
            Assert.Null(Read(Block(payload[..count])));
    }

    [Fact]
    public void UnknownOwnedClassDoesNotBecomeAnEmptySuccessfulRoot()
    {
        Assert.Null(Read(Block(Payload(717, 40, ushort.MaxValue, 0, 0))));
        Assert.Null(Read(Block(Payload(717, 0, 0, 50, ushort.MaxValue))));
    }

    [Fact]
    public void InvalidFooterStillRejectsTheFrame()
    {
        var block = Block(Payload(717, 0, 0, 0, 0)); block[^1] ^= 1;
        Assert.Null(Read(block));
    }

    [Fact]
    public void NonzeroStartAndFollowingBytesDoNotChangeTheInlinePointerQueue()
    {
        var block = Block(Payload(717, 40, 37, 50, 2007));
        var data = new byte[block.Length + 27]; block.CopyTo(data, 9);
        var size = BitConverter.ToUInt32(block, 8);
        Assert.NotNull(TfBlockDeserializer.ExtractParams(data, 9, size, 1309));
        Assert.Equal(new[] { ((ushort)37, 40u), ((ushort)2007, 50u) },
            TfBlockDeserializer.TraceQueue(data, 9, size, 1309));
    }

    [Fact]
    public void SharedRecoveredDescriptorsAreNotMutatedOrGloballyRepaired()
    {
        Assert.NotNull(Read(Block(Payload(717, 0, 0, 0, 0))));
        Assert.Null(TfBlockDeserializer.SerializationFields(1309)!.Single().Ref);
        var fields = TfBlockDeserializer.SerializationFields(1310)!;
        Assert.Null(fields.Single(f => f.Name == "m_pElement").Ref);
        Assert.Equal(1310, fields.Single(f => f.Name == "m_pGRep").Ref);
    }

    private static TfBlockDeserializer.NativeParams? Read(byte[] block)
        => TfBlockDeserializer.ExtractParams(block, 0, BitConverter.ToUInt32(block, 8), 1309);
    private static List<(ushort Cls, uint Id)> Queue(byte[] block)
        => TfBlockDeserializer.TraceQueue(block, 0, BitConverter.ToUInt32(block, 8), 1309);
    private static byte[] Payload(int id, uint element, ushort elementClass, uint grep, ushort grepClass)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(id); writer.Write(element); if (element != 0) writer.Write(elementClass);
        writer.Write(grep); if (grep != 0) writer.Write(grepClass);
        return stream.ToArray();
    }
    private static byte[] Block(byte[] payload)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        uint size = (uint)payload.Length + 2;
        writer.Write(100); writer.Write(0u); writer.Write(size); writer.Write((ushort)1309);
        writer.Write(payload); writer.Write(size); return stream.ToArray();
    }
}
