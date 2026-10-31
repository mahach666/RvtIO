using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class ContentCompactPointerTests
{
    [Theory]
    [InlineData(0UL, 0UL)] [InlineData(1UL, 8UL)]
    [InlineData(0xffffffffffffffffUL, 0x8000000000000000UL)]
    [InlineData(0x000007d7ffffffffUL, 0x00000022ffffffffUL)]
    [InlineData(0x0123456789abcdefUL, 0xfedcba9876543210UL)]
    [InlineData(0x100000000UL, 0xffffffff00000000UL)]
    public void BothCompactWordsRetainTheirExactTwoInt32Pieces(ulong element, ulong grep)
    {
        var block = Block(Payload(0, 0, element, grep, 717, 516));
        var result = Read(block); Assert.NotNull(result); Assert.True(result.RootParameterStoresComplete);
        AssertParts(result, "m_compactElement", element); AssertParts(result, "m_compactGRep", grep);
        Assert.Equal(717, result.Fields["m_elemId"]); Assert.Equal(516, result.Fields["m_epKingId"]);
        Assert.Empty(Queue(block));
    }

    [Theory]
    [InlineData(int.MinValue)] [InlineData(-1)] [InlineData(0)]
    [InlineData(1)] [InlineData(int.MaxValue)]
    public void RecordIdsAreLiteralSignedValuesNotFrameOwnerOrCompactAddresses(int id)
    {
        var result = Read(Block(Payload(0, 0, 17, 33, id, id)));
        Assert.NotNull(result); Assert.Equal(id, result.Fields["m_elemId"]);
        Assert.Equal(id, result.Fields["m_epKingId"]); Assert.Equal(6, result.Fields.Count);
    }

    [Theory]
    [InlineData(0u, 0)] [InlineData(uint.MaxValue, 0)] [InlineData(44u, 1340)]
    public void HeaderPointerGrammarRemainsDistinctFromInlineCompactWords(uint handle, ushort cls)
    {
        var block = Block(Payload(handle, cls, 0x000007d7ffffffff, 0, 717, 516));
        Assert.NotNull(Read(block));
        Assert.Equal(cls == 0 ? Array.Empty<(ushort, uint)>() : new[] { (cls, handle) }, Queue(block));
    }

    [Fact]
    public void EqualWordsRemainTwoIndependentNamedFields()
    {
        var result = Read(Block(Payload(0, 0, 0xfedcba9876543210, 0xfedcba9876543210, 7, 8)));
        Assert.NotNull(result); AssertParts(result, "m_compactElement", 0xfedcba9876543210);
        AssertParts(result, "m_compactGRep", 0xfedcba9876543210); Assert.Equal(6, result.Fields.Count);
    }

    [Fact]
    public void EveryTruncatedRecordByteIsRejectedEvenWithAValidReframedFooter()
    {
        var payload = Payload(0, 0, 0x123456789abcdef0, 0xfedcba9876543210, 717, 516);
        for (int count = 0; count < payload.Length; count++) Assert.Null(Read(Block(payload[..count])));
    }

    [Fact]
    public void UnknownHeaderClassAndBadFooterStillRejectTheRoot()
    {
        Assert.Null(Read(Block(Payload(44, ushort.MaxValue, 1, 2, 7, 8))));
        var block = Block(Payload(0, 0, 1, 2, 7, 8)); block[^1] ^= 1; Assert.Null(Read(block));
    }

    [Fact]
    public void NonzeroStartPreservesAllPiecesAndIds()
    {
        var block = Block(Payload(0, 0, 0x0123456789abcdef, 0xfedcba9876543210, 717, 516));
        var data = new byte[block.Length + 23]; block.CopyTo(data, 9);
        var result = TfBlockDeserializer.ExtractParams(data, 9, BitConverter.ToUInt32(block, 8), 814);
        Assert.NotNull(result); AssertParts(result, "m_compactElement", 0x0123456789abcdef);
        AssertParts(result, "m_compactGRep", 0xfedcba9876543210); Assert.Equal(717, result.Fields["m_elemId"]);
    }

    [Fact]
    public void SharedDescriptorsAndStandalonePointerGrammarRemainUnchanged()
    {
        Assert.NotNull(Read(Block(Payload(0, 0, 0, 0, 7, 8))));
        Assert.Null(TfBlockDeserializer.SerializationFields(814)!.Single(f => f.Name == "m_compactElement").Ref);
        Assert.Equal(815, TfBlockDeserializer.SerializationFields(814)!.Single(f => f.Name == "m_compactGRep").Ref);
        Assert.Equal(815, TfBlockDeserializer.SerializationFields(815)![1].Ref);
        var standalone = Block(BitConverter.GetBytes(0xffffffffffffffffUL), 815);
        Assert.NotNull(TfBlockDeserializer.ExtractParams(standalone, 0, BitConverter.ToUInt32(standalone, 8), 815));
        Assert.Empty(TfBlockDeserializer.TraceQueue(standalone, 0, BitConverter.ToUInt32(standalone, 8), 815));
    }

    private static void AssertParts(TfBlockDeserializer.NativeParams result, string name, ulong word)
    {
        Assert.Equal(unchecked((int)word), result.Fields[name + "@0"]);
        Assert.Equal(unchecked((int)(word >> 32)), result.Fields[name + "@1"]);
    }
    private static TfBlockDeserializer.NativeParams? Read(byte[] block)
        => TfBlockDeserializer.ExtractParams(block, 0, BitConverter.ToUInt32(block, 8), 814);
    private static List<(ushort Cls, uint Id)> Queue(byte[] block)
        => TfBlockDeserializer.TraceQueue(block, 0, BitConverter.ToUInt32(block, 8), 814);
    private static byte[] Payload(uint header, ushort cls, ulong element, ulong grep, int id, int episode)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(header); if (header != 0) writer.Write(cls);
        writer.Write(element); writer.Write(grep); writer.Write(id); writer.Write(episode);
        return stream.ToArray();
    }
    private static byte[] Block(byte[] payload, ushort cls = 814)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        uint size = (uint)payload.Length + 2;
        writer.Write(100); writer.Write(0u); writer.Write(size); writer.Write(cls);
        writer.Write(payload); writer.Write(size); return stream.ToArray();
    }
}
