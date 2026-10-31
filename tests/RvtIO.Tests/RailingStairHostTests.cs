using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class RailingStairHostTests
{
    [Theory]
    [InlineData(456)] [InlineData(-1)] [InlineData(0)]
    public void NamedHostIsNotAnOwnerAdjustmentComponentOrRegisteredList(int host)
    {
        byte[] bytes = Frame(host);
        Assert.True(TfBlockDeserializer.TryExtractRailingStairHost(bytes, 0, Size(bytes), 485, out int value));
        Assert.Equal(host, value);
    }
    [Theory]
    [InlineData(0, 576198, 576198)]   // legacy format: no m_stairsId, the stair is the component id
    [InlineData(-1, 576198, 576198)]
    [InlineData(456, 576198, 456)]
    [InlineData(0, -1, 0)]
    [InlineData(-1, -1, -1)]
    public void LegacyRailingKeepsItsStairInTheComponentId(int host, int component, int expected)
    {
        byte[] bytes = Frame(host, component);
        Assert.True(TfBlockDeserializer.TryExtractRailingStairHost(bytes, 0, Size(bytes), 485, out int value));
        Assert.Equal(expected, value);
    }
    [Theory]
    [InlineData("doc")] [InlineData("owner")] [InlineData("footer")]
    [InlineData("size")] [InlineData("class")] [InlineData("count")]
    public void MalformedFramingOrOwnPrefixFailsClosed(string mode)
    {
        byte[] bytes = Frame(456);
        int offset = mode switch { "doc" => 46, "owner" => 50, "footer" => bytes.Length - 1,
            "size" => 8, "class" => 12, _ => 85 };
        if (mode == "count") BitConverter.GetBytes(100001u).CopyTo(bytes, offset);
        else bytes[offset] ^= 1;
        Assert.False(TfBlockDeserializer.TryExtractRailingStairHost(bytes, 0, Size(bytes), 485, out int value));
        Assert.Equal(-1, value);
    }
    [Fact]
    public void EveryTruncatedPrefixRejectsWithoutPartialHost()
    {
        byte[] original = Frame(456);
        for (int end = 14; end < original.Length - 4; end++)
        {
            byte[] bytes = [.. original[..end], 0, 0, 0, 0];
            uint size = (uint)bytes.Length - 16;
            BitConverter.GetBytes(size).CopyTo(bytes, 8); BitConverter.GetBytes(size).CopyTo(bytes, bytes.Length - 4);
            Assert.False(TfBlockDeserializer.TryExtractRailingStairHost(bytes, 0, size, 485, out int host));
            Assert.Equal(-1, host);
        }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OwnHostMetadataRequiresUniqueMaterializationAndSemanticClass(bool otherClassFirst)
    {
        var state = new ParsingState { ElemRecOwningElementIds = new() { [123] = -1 } };
        byte[] bytes = Frame(456);
        if (otherClassFirst)
        {
            BitConverter.GetBytes((ushort)157).CopyTo(bytes, 12);
            StairComponentSource.Record(state, bytes, 0, Size(bytes), 157, 123);
            BitConverter.GetBytes((ushort)485).CopyTo(bytes, 12);
        }
        StairComponentSource.Record(state, bytes, 0, Size(bytes), 485, 123);
        var elements = new List<Element> { new() { Id = 123 } };
        StairComponentSource.Apply(elements, state);
        if (!otherClassFirst) Assert.Equal(456, elements[0].SerializedRailingStairHostId);
        // A non-railing semantic root with the same owner is also ambiguous.
        BitConverter.GetBytes((ushort)157).CopyTo(bytes, 12);
        StairComponentSource.Record(state, bytes, 0, Size(bytes), 157, 123);
        StairComponentSource.Apply(elements, state);
        Assert.Null(elements[0].SerializedRailingStairHostId);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SeparateGElementDoesNotCreateASemanticDuplicate(bool geometryFirst)
    {
        var state = new ParsingState { ElemRecOwningElementIds = new() { [123] = -1 } };
        byte[] bytes = Frame(456);
        byte[] geometry = (byte[])bytes.Clone(); BitConverter.GetBytes((ushort)2007).CopyTo(geometry, 12);
        if (geometryFirst) StairComponentSource.Record(state, geometry, 0, Size(geometry), 2007, 123);
        StairComponentSource.Record(state, bytes, 0, Size(bytes), 485, 123);
        if (!geometryFirst) StairComponentSource.Record(state, geometry, 0, Size(geometry), 2007, 123);
        var element = new Element { Id = 123 };
        StairComponentSource.Apply([element], state);
        Assert.Equal(456, element.SerializedRailingStairHostId);
        Assert.Equal(1, state.OwnedElementClassRoots[123].BlockCount);
    }
    [Fact]
    public void MalformedDuplicatePermanentlyClearsHostSource()
    {
        var state = new ParsingState { ElemRecOwningElementIds = new() { [123] = -1 } };
        byte[] bytes = Frame(456);
        StairComponentSource.Record(state, bytes, 0, Size(bytes), 485, 123);
        bytes[^1] ^= 1;
        StairComponentSource.Record(state, bytes, 0, Size(bytes), 485, 123);
        Assert.Equal((2, (int?)null), state.RailingStairHostRoots[123]);
    }
    private static uint Size(byte[] bytes) => BitConverter.ToUInt32(bytes, 8);
    private static byte[] Frame(int host, int? component = null)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(123u); writer.Write(0u); writer.Write(0u); writer.Write((ushort)485);
        for (int index = 0; index < 6; index++) writer.Write(0u);
        writer.Write(0u); writer.Write(0u); writer.Write(1u); writer.Write(123);
        for (int index = 1; index < 8; index++) writer.Write(-1);
        writer.Write(new byte[3]);
        for (int index = 0; index < 6; index++) writer.Write(0u); // counts and point driver
        for (int index = 0; index < 12; index++) writer.Write(0.0); // Trf
        writer.Write(0.0); writer.Write(789); writer.Write(host);
        // m_placementOffset, m_sketchId, m_stairsComponentId
        if (component is int stairsComponent) { writer.Write(0.0); writer.Write(-1); writer.Write(stairsComponent); }
        uint size = (uint)stream.Length - 12; writer.Write(size);
        stream.Position = 8; writer.Write(size); return stream.ToArray();
    }
}
