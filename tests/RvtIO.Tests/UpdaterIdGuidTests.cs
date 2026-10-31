using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class UpdaterIdGuidTests
{
    [Theory]
    [InlineData(34, 16)] [InlineData(1229, 32)]
    public void OriginalInlineGuidWrappersReadWithoutRecursingIntoTheirOwnDescriptors(int cls, int length)
    {
        var bytes = Block((ushort)cls, Payload(length));
        Assert.NotNull(Read(bytes));
        Assert.Empty(TfBlockDeserializer.TraceQueue(bytes, 0, BitConverter.ToUInt32(bytes, 8), (ushort)cls));
        Assert.Equal(cls, TfBlockDeserializer.SerializationFields(cls)!.Single(f => f.Name == "m_value").Ref);
        // The shared descriptor remains unchanged; only its exact reader contract is corrected.
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)] [InlineData(12)]
    public void CountedDynamicUpdaterIdsConsumeEachPairOfGuidsWithoutAddingOwnedObjects(int count)
    {
        var payload = BitConverter.GetBytes((uint)count).Concat(Payload(32 * count)).ToArray();
        var bytes = Block(1231, payload);
        var result = Read(bytes);
        Assert.NotNull(result); Assert.True(result.RootParameterStoresComplete);
        Assert.Empty(TfBlockDeserializer.TraceQueue(bytes, 0, BitConverter.ToUInt32(bytes, 8), 1231));
    }

    [Theory]
    [InlineData(34, 16)] [InlineData(1229, 32)]
    public void EveryTruncatedGuidByteIsRejectedEvenWithAReframedSizeFooter(int cls, int length)
    {
        for (int prefix = 0; prefix < length; prefix++)
            Assert.Null(Read(Block((ushort)cls, Payload(prefix))));
    }

    [Fact]
    public void TruncatedLastUpdaterGuidDoesNotProduceACompletedRoot()
    {
        for (int prefix = 0; prefix < 64; prefix++)
            Assert.Null(Read(Block(1231, BitConverter.GetBytes(2u).Concat(Payload(prefix)).ToArray())));
    }

    [Theory]
    [InlineData(0)] [InlineData(255)]
    public void PointerLikeGuidBitsAreInlineValuesNotClassTagsOrDeferredQueueHandles(byte fill)
    {
        var payload = Enumerable.Repeat(fill, 32).ToArray();
        BitConverter.GetBytes(uint.MaxValue).CopyTo(payload, 0);
        BitConverter.GetBytes((ushort)4).CopyTo(payload, 4);
        BitConverter.GetBytes((ushort)0x0108).CopyTo(payload, 6);
        var bytes = Block(1229, payload);
        Assert.NotNull(Read(bytes));
        Assert.Empty(TfBlockDeserializer.TraceQueue(bytes, 0, BitConverter.ToUInt32(bytes, 8), 1229));
    }

    [Theory]
    [InlineData(34, 16)] [InlineData(1229, 32)]
    public void NonzeroBlockStartUsesTheSameInlineWidths(int cls, int length)
    {
        var bytes = Block((ushort)cls, Payload(length)); var segment = new byte[bytes.Length + 17]; bytes.CopyTo(segment, 9);
        var result = TfBlockDeserializer.ExtractParams(segment, 9, BitConverter.ToUInt32(bytes, 8), (ushort)cls);
        Assert.NotNull(result); Assert.True(result.RootParameterStoresComplete);
    }

    [Fact]
    public void BadFooterIsNotPromotedByTheGuidCorrection()
    {
        var bytes = Block(1229, Payload(32)); bytes[^1] ^= 1;
        Assert.Null(Read(bytes));
    }

    private static byte[] Payload(int length) => Enumerable.Range(0, length).Select(i => (byte)(i * 37 + 11)).ToArray();
    private static TfBlockDeserializer.NativeParams? Read(byte[] bytes)
        => TfBlockDeserializer.ExtractParams(bytes, 0, BitConverter.ToUInt32(bytes, 8), BitConverter.ToUInt16(bytes, 12));
    private static byte[] Block(ushort cls, byte[] payload)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        uint size = (uint)payload.Length + 2;
        writer.Write(100); writer.Write(0u); writer.Write(size); writer.Write(cls);
        writer.Write(payload); writer.Write(size); return stream.ToArray();
    }
}
