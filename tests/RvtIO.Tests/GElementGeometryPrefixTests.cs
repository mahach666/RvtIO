using System.Text.Json;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class GElementGeometryPrefixTests
{
    [Fact]
    public void FirstDirectGeometryUsesOrderedQueueIdentityNotDuplicateHandlesOrNestedChildren()
    {
        var (bytes, offsets) = Block();
        var value = Read(bytes, out var failure)!;
        Assert.Null(failure); Assert.Equal(93, value.OwnerId); Assert.Equal(0xdecafbadU, value.OpaqueHeader);
        Assert.Equal(2, value.SelectedPointer!.QueueIndex); Assert.Equal((ushort)2092, value.SelectedPointer.ClassId);
        Assert.All(value.SubNodes.Members, p => Assert.Equal(uint.MaxValue, p.Handle));
        Assert.Equal(new[] { 0, 1, 2, 3 }, value.SubNodes.Members.Select(p => p.QueueIndex!.Value));
        Assert.Equal(offsets["root-end"], value.RootEnd); Assert.Equal(offsets["prefix-end"], value.End);
        Assert.Equal(new ushort[] { 2015, 1991 }, value.PriorNodes.Select(p => p.ClassId));
        Assert.Equal(4, Assert.Single(value.PriorNodes[0].SubNodes!.Members).QueueIndex);
        Assert.Equal(5, Assert.Single(value.PriorNodes[0].Conditions!.Members).QueueIndex);
        Assert.Equal(6, value.PriorNodes[1].InstanceInfo!.QueueIndex);
        var geometry = value.Geometry!;
        Assert.Equal(2, geometry.QueueIndex); Assert.Equal(offsets["geometry"], geometry.Start);
        Assert.Equal(-77, geometry.GInfo.Tag); Assert.Equal(int.MinValue, geometry.GInfo.Control);
        Assert.Equal(int.MaxValue, geometry.GInfo.RenderStyleId); Assert.Equal(uint.MaxValue, geometry.GInfo.Flags);
        Assert.Equal(-4, geometry.Flags); Assert.Equal(int.MinValue, geometry.GeometryTag);
        Assert.Equal(int.MaxValue, geometry.TessellationType); Assert.Equal(-1, geometry.TessellationVersion);
        Assert.Equal(new int?[] { 7, 8, null }, geometry.Faces.Members.Select(p => p.QueueIndex));
        Assert.Equal(new int?[] { 9, 10, null }, geometry.Edges.Members.Select(p => p.QueueIndex));
        Assert.Equal((ushort)0, geometry.Faces.Members[2].ClassId);
        Assert.Equal(0u, geometry.Edges.Members[2].Handle);
        Assert.Equal(geometry.End, value.End); Assert.True(value.End < bytes.Length - 4);
    }

    [Fact]
    public void RawGInfoFlagsAndBoxBitsAreNotFilteredAsGeometryValidityHeuristics()
    {
        var (bytes, _) = Block(); var value = Read(bytes, out _)!;
        Assert.Equal(-99, value.GInfo.Tag); Assert.Equal(int.MinValue, value.GInfo.Control);
        Assert.Equal(int.MaxValue, value.GInfo.RenderStyleId); Assert.Equal(uint.MaxValue, value.GInfo.Flags);
        long[] bits = [long.MinValue, 0x7ff8000000000042L, 0x7ff0000000000000L,
            unchecked((long)0xfff0000000000000UL), 0, long.MaxValue];
        Assert.Equal(bits, value.BoxBits); Assert.Equal(bits.AsEnumerable().Reverse(), value.TightBoxBits);
        Assert.Equal(int.MinValue, value.ElementType); Assert.Equal(uint.MaxValue, value.Flags);
        Assert.Equal(16, value.GInfo.End - value.GInfo.Start);
    }

    [Theory]
    [InlineData(0u)] [InlineData(uint.MaxValue)]
    public void NullAndEmptyGeometryArraysHaveDifferentRawCounts(uint count)
    {
        var (bytes, _) = Block(geometryCount: count); var value = Read(bytes, out var failure)!;
        Assert.Null(failure);
        foreach (var array in new[] { value.Geometry!.Faces, value.Geometry.Edges, value.Geometry.SharedSurfaceInfo })
        { Assert.Equal(count, array.Count); Assert.Empty(array.Members); Assert.Equal(4, array.End - array.Start); }
    }

    [Theory]
    [InlineData(0u)] [InlineData(uint.MaxValue)]
    public void EmptyRootStopsAtRootWithoutClaimingAFullGraph(uint count)
    {
        var (bytes, _) = Block(rootCount: count); var value = Read(bytes, out var failure)!;
        Assert.Null(failure); Assert.Null(value.Geometry); Assert.Null(value.SelectedPointer);
        Assert.Equal(count, value.SubNodes.Count); Assert.Empty(value.PriorNodes);
        Assert.Equal(value.RootEnd, value.End);
    }

    [Theory]
    [InlineData(0)] [InlineData(4)] [InlineData(1589)] [InlineData(1978)]
    public void UnresolvedOrUnsupportedPrecedingKindIsNotSkipped(int kind)
    {
        var (bytes, _) = Block(firstKind: (ushort)kind);
        Assert.Null(Read(bytes, out var failure)); Assert.Equal($"subnode-kind:0:{kind}", failure);
    }

    [Fact]
    public void UnsupportedKindAfterFirstGeometryDoesNotReplaceTheSelectedGeometry()
    {
        var (bytes, _) = Block(lastKind: 1589); var value = Read(bytes, out var failure)!;
        Assert.Null(failure); Assert.Equal(2, value.SelectedPointer!.QueueIndex);
        Assert.Equal((ushort)1589, value.SubNodes.Members[3].ClassId);
    }

    [Fact]
    public void NullSubnodeBeforeGeometryIsRetainedWithoutAllocatingAQueueEntry()
    {
        var (bytes, _) = Block(firstNull: true); var value = Read(bytes, out var failure)!;
        Assert.Null(failure); Assert.Equal(0u, value.SubNodes.Members[0].Handle);
        Assert.Null(value.SubNodes.Members[0].QueueIndex); Assert.Equal(1, value.SelectedPointer!.QueueIndex);
        Assert.Single(value.PriorNodes); Assert.Equal((ushort)1991, value.PriorNodes[0].ClassId);
    }

    [Theory]
    [InlineData("root-count")] [InlineData("filter-subnodes")] [InlineData("filter-conditions")]
    [InlineData("faces")] [InlineData("edges")] [InlineData("shared")]
    public void OversizedCountsFailWithoutPartialValidation(string field)
    {
        var (bytes, offsets) = Block(); BitConverter.GetBytes(100001u).CopyTo(bytes, offsets[field]);
        Assert.Null(Read(bytes, out var failure)); Assert.NotNull(failure);
    }

    [Theory]
    [InlineData("root-count")] [InlineData("faces")] [InlineData("edges")]
    public void SmallCountsStillRequireEnoughRemainingPointerBytes(string field)
    {
        var (bytes, offsets) = Block(); BitConverter.GetBytes(100000u).CopyTo(bytes, offsets[field]);
        Assert.Null(Read(bytes, out var failure)); Assert.NotNull(failure);
    }

    [Fact]
    public void EveryPrefixByteTruncationFailsEvenWithAReframedValidFooter()
    {
        var (bytes, offsets) = Block();
        for (int end = 14; end < offsets["prefix-end"]; end++)
        {
            byte[] cut = bytes.AsSpan(0, end).ToArray().Concat(new byte[4]).ToArray(); Frame(cut);
            Assert.Null(Read(cut, out var failure)); Assert.NotNull(failure);
        }
    }

    [Theory]
    [InlineData("owner")] [InlineData("footer")] [InlineData("class")]
    [InlineData("size")] [InlineData("unknown-pointer")]
    public void InvalidFramingOrUnknownOwnedClassDoesNotProduceAPrefix(string mutation)
    {
        var (bytes, offsets) = Block();
        if (mutation == "owner") BitConverter.GetBytes(-1).CopyTo(bytes, 0);
        if (mutation == "footer") bytes[^1] ^= 1;
        if (mutation == "class") BitConverter.GetBytes((ushort)2006).CopyTo(bytes, 12);
        if (mutation == "size") bytes[8] ^= 1;
        if (mutation == "unknown-pointer") BitConverter.GetBytes(ushort.MaxValue).CopyTo(bytes, offsets["root-count"] + 8);
        Assert.Null(Read(bytes, out var failure)); Assert.NotNull(failure);
    }

    [Theory]
    [InlineData(-1)] [InlineData(int.MaxValue)]
    public void InvalidFrameStartIsRejectedWithoutOverflow(int start)
    {
        var (bytes, _) = Block();
        Assert.Null(TfBlockDeserializer.ExtractOwnGElementGeometryPrefix(bytes, start,
            BitConverter.ToUInt32(bytes, 8), 2007, out var failure)); Assert.Equal("frame", failure);
    }

    [Fact]
    public void FrameRelativeSpansAndRawValuesAreIdenticalAtNonzeroStart()
    {
        var (bytes, _) = Block(); var embedded = new byte[bytes.Length + 31]; bytes.CopyTo(embedded, 17);
        var actual = TfBlockDeserializer.ExtractOwnGElementGeometryPrefix(embedded, 17,
            BitConverter.ToUInt32(bytes, 8), 2007, out var failure);
        Assert.Null(failure); Assert.Equal(JsonSerializer.Serialize(Read(bytes, out _)), JsonSerializer.Serialize(actual));
    }

    [Fact]
    public void PositiveRepeatedOwnedHandlesStillHaveDistinctDeferredQueueIndices()
    {
        var (bytes, offsets) = Block();
        for (int i = 0; i < 4; i++) BitConverter.GetBytes(7u).CopyTo(bytes, offsets["root-count"] + 4 + i * 6);
        var value = Read(bytes, out var failure)!; Assert.Null(failure);
        Assert.All(value.SubNodes.Members, p => Assert.Equal(7u, p.Handle));
        Assert.Equal(new[] { 0, 1, 2, 3 }, value.SubNodes.Members.Select(p => p.QueueIndex!.Value));
        Assert.Equal(2, value.Geometry!.QueueIndex);
    }

    [Fact]
    public void PriorExternalInstancePointerIsRetainedWithoutADeferredPayload()
    {
        var (bytes, offsets) = Block(); BitConverter.GetBytes((ushort)0).CopyTo(bytes, offsets["geometry"] - 2);
        var value = Read(bytes, out var failure)!; Assert.Null(failure);
        var instance = value.PriorNodes[1].InstanceInfo!;
        Assert.Equal(uint.MaxValue, instance.Handle); Assert.Equal((ushort)0, instance.ClassId);
        Assert.Null(instance.QueueIndex); Assert.Equal(6, value.Geometry!.Faces.Members[0].QueueIndex);
    }

    [Fact]
    public void NonemptySharedSurfaceSlotsAreRetainedWithoutInterpretingTheirLaterPayload()
    {
        var (bytes, _) = Block(shared: true); var value = Read(bytes, out var failure)!;
        Assert.Null(failure); var pointer = Assert.Single(value.Geometry!.SharedSurfaceInfo.Members);
        Assert.Equal(1u, value.Geometry.SharedSurfaceInfo.Count); Assert.Equal((ushort)2092, pointer.ClassId);
        Assert.Equal(11, pointer.QueueIndex); Assert.Equal(6, pointer.End - pointer.Start);
    }

    [Theory]
    [InlineData(uint.MaxValue)] [InlineData(0x80000000u)]
    public void ExtremeReportedFrameSizesDoNotOverflowBounds(uint size)
    {
        var (bytes, _) = Block();
        Assert.Null(TfBlockDeserializer.ExtractOwnGElementGeometryPrefix(bytes, 0,size,2007,out var failure));
        Assert.Equal("frame", failure);
    }

    private static TfBlockDeserializer.OwnGElementGeometryPrefix? Read(byte[] bytes, out string? failure)
        => TfBlockDeserializer.ExtractOwnGElementGeometryPrefix(bytes, 0, BitConverter.ToUInt32(bytes, 8),
            BitConverter.ToUInt16(bytes, 12), out failure);

    private static void Frame(byte[] bytes)
    {
        uint size = (uint)bytes.Length - 16;
        BitConverter.GetBytes(size).CopyTo(bytes, 8); BitConverter.GetBytes(size).CopyTo(bytes, bytes.Length - 4);
    }

    private static (byte[] Bytes, Dictionary<string,int> Offsets) Block(uint? geometryCount = null,
        uint? rootCount = null, ushort firstKind = 2015, ushort lastKind = 2092, bool firstNull = false,
        bool shared = false)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        var offsets = new Dictionary<string,int>();
        void Mark(string name) => offsets[name] = (int)stream.Position;
        void Info(int tag) { w.Write(tag); w.Write(int.MinValue); w.Write(int.MaxValue); w.Write(uint.MaxValue); }
        void Pointer(uint handle, ushort cls) { w.Write(handle); if (handle != 0) { w.Write(cls); if (cls == 4) w.Write((ushort)8); } }
        void Array(string name, uint count, Action body) { Mark(name); w.Write(count); if (count != uint.MaxValue && count != 0) body(); }
        w.Write(93); w.Write(0xdecafbadU); w.Write(0u); w.Write((ushort)2007); Info(-99);
        Array("root-count",rootCount ?? 4, () =>
        {
            Pointer(firstNull ? 0u : uint.MaxValue, firstKind); Pointer(uint.MaxValue,1991);
            Pointer(uint.MaxValue,2092); Pointer(uint.MaxValue,lastKind);
        });
        long[] bits = [long.MinValue, 0x7ff8000000000042L, 0x7ff0000000000000L,
            unchecked((long)0xfff0000000000000UL), 0, long.MaxValue];
        foreach (long bit in bits) w.Write(bit); foreach (long bit in bits.AsEnumerable().Reverse()) w.Write(bit);
        w.Write(int.MinValue); w.Write(uint.MaxValue); Mark("root-end");
        if (!firstNull)
        {
            Info(-2); Array("filter-subnodes",1, () => Pointer(uint.MaxValue,2092));
            Array("filter-conditions",1, () => Pointer(uint.MaxValue,1996));
        }
        Info(-3); Pointer(uint.MaxValue,2238); Mark("geometry"); Info(-77);
        Array("faces",geometryCount ?? 3, () =>
        { Pointer(uint.MaxValue,1589); Pointer(uint.MaxValue,1589); Pointer(17,0); });
        w.Write(-4); w.Write(int.MinValue); w.Write(int.MaxValue); w.Write(-1);
        Array("edges",geometryCount ?? 3, () =>
        { Pointer(uint.MaxValue,1243); Pointer(uint.MaxValue,1243); Pointer(0,0); });
        Array("shared",shared ? 1 : geometryCount ?? 0, () => Pointer(uint.MaxValue,2092)); Mark("prefix-end");
        w.Write(new byte[] { 0x11,0x22,0x33 }); // subsequent payload, intentionally outside the prefix
        w.Write(0u); byte[] bytes = stream.ToArray(); Frame(bytes); return (bytes, offsets);
    }
}
