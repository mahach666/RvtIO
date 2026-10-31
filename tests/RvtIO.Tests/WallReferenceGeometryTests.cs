using System.Text.Json;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class WallReferenceGeometryTests
{
    [Theory]
    [InlineData(3524)] [InlineData(361)]
    public void ReadsOwnReferenceFacesAndDriverCellPointersThroughLastDirectInput(int cls)
    {
        var (bytes, offsets) = Block((ushort)cls);
        var value = Read(bytes, out var failure)!;
        Assert.Null(failure); Assert.Equal(100, value.OwnerId); Assert.Equal(cls, value.ClassId);
        Assert.Equal(offsets["root-end"], value.RootEnd);
        Assert.Equal(offsets["prefix-end"], value.End);
        Assert.True(value.End < bytes.Length - 4); // later cells/curves/surfaces are not decoded
        Assert.Equal(new ushort[] { 2086, 653, 4040, 1589, 1589 }, value.Objects.Select(o => o.ClassId));
        Assert.Equal(Enumerable.Range(0, 5), value.Objects.Select(o => o.QueueIndex));
        Assert.Equal(2u, value.ReferenceFaces.Count);
        Assert.Equal(new int?[] { 3, 4 }, value.ReferenceFaces.Members.Select(p => p.QueueIndex));
        Assert.All(value.Faces, f => Assert.Equal(7u, f.Handle)); // duplicate handles do not merge definitions
        Assert.Equal(new[] { -77, int.MaxValue }, value.Faces.Select(f => f.Tag));
        Assert.All(value.Faces, f => Assert.Equal(int.MinValue, f.CategoryId));
        Assert.All(value.Faces, f => Assert.Equal(-1, f.SubcategoryId));
        Assert.All(value.Faces, f => Assert.Equal(0xfedcba98u, f.FaceFlags));
        Assert.All(value.Faces, f => Assert.Equal(-333, f.RenderStyleId));
        Assert.All(value.Faces, f => Assert.Equal(int.MinValue, f.CutType));
        Assert.All(value.Faces, f => Assert.Equal((ushort?)522, f.Surface.ClassId));
        Assert.All(value.Faces, f => Assert.True(f.Surface.QueueIndex > value.Objects[^1].QueueIndex));
        var driver = Assert.Single(value.CurveDrivers);
        Assert.Equal(value.CurveDriverPointer.QueueIndex, driver.QueueIndex);
        Assert.Equal(0u, driver.ControlData.Handle);
        Assert.Equal((ushort?)1736, driver.Curve.ClassId);
        Assert.True(driver.Curve.QueueIndex > value.Objects[^1].QueueIndex);
        var cellList = Assert.Single(value.CellLists);
        Assert.Equal(value.CellListPointer.QueueIndex, cellList.QueueIndex);
        Assert.Equal((ushort?)953, Assert.Single(cellList.Cells.Members).ClassId);
        Assert.Equal(42u, value.ListOwnerReference); // not hardcoded 2 or substituted ElementId100
        Assert.All(value.Objects.Zip(value.Objects.Skip(1)), p => Assert.Equal(p.First.End, p.Second.Start));
    }

    [Theory]
    [InlineData(0u)] [InlineData(1u)] [InlineData(0x00080000u)]
    [InlineData(0x00080004u)] [InlineData(uint.MaxValue)]
    public void InlineGInfoDoesNotDependOnCategoryOrFlagPattern(uint flags)
    {
        var (bytes, offsets) = Block();
        BitConverter.GetBytes(flags).CopyTo(bytes, offsets["face-flags"]);
        Assert.Equal(flags, Read(bytes, out _)!.Faces[0].Flags);
        Assert.Equal(int.MinValue, Read(bytes, out _)!.Faces[0].CategoryId);
    }

    [Theory]
    [InlineData("null-reference-array")] [InlineData("empty-reference-array")]
    public void NullArrayAndEmptyArrayRemainDistinct(string mutation)
    {
        var value = Read(Block(mutation: mutation).Bytes, out var failure)!;
        Assert.Null(failure); Assert.Empty(value.ReferenceFaces.Members); Assert.Empty(value.Faces);
        Assert.Equal(mutation.StartsWith("null") ? uint.MaxValue : 0u, value.ReferenceFaces.Count);
        Assert.Single(value.CurveDrivers); Assert.Single(value.CellLists);
    }

    [Theory]
    [InlineData("external-cell")] [InlineData("null-cell")]
    public void UnresolvedRootPointersRemainRawNotLinkedToAnUnrelatedObject(string mutation)
    {
        var value = Read(Block(mutation: mutation).Bytes, out var failure)!;
        Assert.Null(failure); Assert.Empty(value.CellLists); Assert.Null(value.CellListPointer.QueueIndex);
        Assert.Equal(mutation == "null-cell" ? 0u : 99u, value.CellListPointer.Handle);
        Assert.Single(value.CurveDrivers); Assert.Equal(2, value.Faces.Count);
    }

    [Theory]
    [InlineData("hand-loop", 8)] [InlineData("extended-hand-loop", 64)]
    public void HandPointersRetainTheirWholeSpanAndDoNotAddFifoObjects(string mutation, int length)
    {
        var value = Read(Block(mutation: mutation).Bytes, out var failure)!;
        Assert.Null(failure); Assert.Equal(5, value.Objects.Count);
        var pointer = value.Faces[0].FirstLoop;
        Assert.Equal((ushort?)4, pointer.ClassId); Assert.Null(pointer.QueueIndex);
        Assert.Equal(length, pointer.End - pointer.Start);
    }

    [Theory]
    [InlineData(0u)] [InlineData(1u)] [InlineData(2u)] [InlineData(uint.MaxValue)]
    public void ListOwnerReferenceRemainsRaw(uint reference)
    {
        var (bytes, offsets) = Block(); BitConverter.GetBytes(reference).CopyTo(bytes, offsets["owner-reference"]);
        Assert.Equal(reference, Read(bytes, out _)!.ListOwnerReference);
    }

    [Fact]
    public void NonzeroFrameStartDoesNotChangeRelativeSpansOrValues()
    {
        var (bytes, _) = Block(361); var segment = new byte[bytes.Length + 17]; bytes.CopyTo(segment, 9);
        var actual = TfBlockDeserializer.ExtractOwnWallReferenceGeometryInputs(segment, 9,
            BitConverter.ToUInt32(bytes, 8), 361, out var failure);
        Assert.Null(failure);
        Assert.Equal(JsonSerializer.Serialize(Read(bytes, out _)), JsonSerializer.Serialize(actual));
    }

    [Theory]
    [InlineData("owner")] [InlineData("footer")] [InlineData("size")]
    [InlineData("class")] [InlineData("foreign-document")] [InlineData("null-list")]
    [InlineData("external-list")] [InlineData("handle-list")]
    [InlineData("huge-references")] [InlineData("short-face")]
    [InlineData("huge-regions")] [InlineData("unknown-surface")]
    [InlineData("invalid-hand-loop")] [InlineData("short-driver")]
    [InlineData("unsupported-prior-node")]
    public void InvalidInputHasNoPartiallyPromotedPrefix(string mutation)
    {
        Assert.Null(Read(Block(mutation: mutation).Bytes, out var failure));
        Assert.False(string.IsNullOrEmpty(failure));
    }

    [Theory]
    [InlineData(-1, 8u, 3524)] [InlineData(0, 0u, 3524)] [InlineData(0, uint.MaxValue, 3524)]
    [InlineData(0, 8u, 1794)] [InlineData(int.MaxValue, 8u, 3524)]
    public void InvalidFramingArgumentsDoNotThrow(int start, uint size, int cls)
    {
        Assert.Null(TfBlockDeserializer.ExtractOwnWallReferenceGeometryInputs(new byte[32], start,
            size, (ushort)cls, out var failure)); Assert.Equal("frame", failure);
    }

    private static TfBlockDeserializer.OwnWallReferenceGeometryInputs? Read(byte[] bytes, out string? failure)
        => TfBlockDeserializer.ExtractOwnWallReferenceGeometryInputs(bytes, 0, BitConverter.ToUInt32(bytes, 8),
            BitConverter.ToUInt16(bytes, 12), out failure);

    private static (byte[] Bytes, Dictionary<string, int> Offsets) Block(ushort cls = 3524, string? mutation = null)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        var offsets = new Dictionary<string, int>(); var queue = new List<ushort>();
        w.Write(100); w.Write(0xdecafbadU); w.Write(0u); w.Write(cls);
        foreach (var field in TfBlockDeserializer.SerializationFields(cls)!)
        {
            if (field.Name == "m_pParamValueSetDouble" && mutation == "unsupported-prior-node") Pointer(2007);
            else if (field.Declaring == 37 && field.Name == "m_id") w.Write(100);
            else if (field.Declaring == 37 && field.Name == "m_docAccess") w.Write(mutation == "foreign-document" ? 2u : 1u);
            else if (field.Declaring == 37 && field.Name == "m_geomSteps")
            {
                if (mutation == "null-list") w.Write(0u);
                else Pointer(mutation == "external-list" ? (ushort)0 : (ushort)2086,
                    mutation == "handle-list" ? 77u : uint.MaxValue);
            }
            else if (field.Declaring == 37 && field.Name == "m_cellList")
            {
                if (mutation == "null-cell") w.Write(0u);
                else Pointer(mutation == "external-cell" ? (ushort)0 : (ushort)653,
                    mutation == "external-cell" ? 99u : uint.MaxValue);
            }
            else if (field.Declaring == 362 && field.Name == "m_pCurveDriver") Pointer(4040);
            else if (field.Declaring == 362 && field.Name == "m_pRefFaces")
            {
                w.Write(mutation == "null-reference-array" ? uint.MaxValue
                    : mutation == "empty-reference-array" ? 0u : mutation == "huge-references" ? 100_001u : 2u);
                if (mutation is not ("null-reference-array" or "empty-reference-array" or "huge-references"))
                { Pointer(1589, 7); Pointer(1589, 7); }
            }
            else Default(field);
        }
        offsets["root-end"] = (int)stream.Position;
        int initial = queue.Count, face = 0;
        for (int index = 0; index < initial; index++)
        {
            ushort kind = queue[index];
            if (kind == 2007) w.Write(new byte[32]); // known schema, but outside this prefix's admission
            else if (kind == 2086)
                foreach (var field in TfBlockDeserializer.SerializationFields(kind)!)
                    if (field.Name == "m_pElem") { offsets["owner-reference"] = (int)stream.Position; w.Write(42u); }
                    else Default(field);
            else if (kind == 653) { w.Write(1u); Pointer(953); }
            else if (kind == 4040)
            {
                if (mutation == "short-driver") { w.Write(0u); return Finish(); }
                foreach (var field in TfBlockDeserializer.SerializationFields(kind)!)
                    if (field.Name == "m_pCrv") Pointer(1736);
                    else Default(field);
            }
            else if (kind == 1589)
            {
                if (mutation == "short-face") { w.Write(0); return Finish(); }
                w.Write(face++ == 0 ? -77 : int.MaxValue); w.Write(int.MinValue); w.Write(-1);
                if (face == 1) offsets["face-flags"] = (int)stream.Position;
                w.Write(0xdeadbeefu);
                if (mutation is "hand-loop" or "extended-hand-loop" or "invalid-hand-loop")
                {
                    Pointer(4); w.Write(mutation == "hand-loop" ? (ushort)8
                        : mutation == "extended-hand-loop" ? (ushort)0x0108 : (ushort)0x0208);
                    if (mutation == "extended-hand-loop") w.Write(new byte[56]);
                }
                else w.Write(0u);
                w.Write(mutation == "huge-regions" ? 100_001u : uint.MaxValue);
                w.Write(0u); w.Write(0u); w.Write(int.MinValue); w.Write(0xfedcba98u); w.Write(-333);
                Pointer(mutation == "unknown-surface" ? ushort.MaxValue : (ushort)522);
            }
            else throw new InvalidOperationException(kind.ToString());
        }
        offsets["prefix-end"] = (int)stream.Position;
        w.Write(new byte[] { 0xff, 0xde, 0xad, 0xbe, 0xef }); // deliberately unsupported later FIFO
        return Finish();

        void Pointer(ushort kind, uint handle = uint.MaxValue)
        {
            w.Write(handle); w.Write(kind);
            if (kind is not (0 or 4)) queue.Add(kind);
        }
        (byte[], Dictionary<string, int>) Finish()
        {
            w.Write(0u); var bytes = stream.ToArray(); uint size = (uint)bytes.Length - 16;
            BitConverter.GetBytes(size).CopyTo(bytes, 8); BitConverter.GetBytes(size).CopyTo(bytes, bytes.Length - 4);
            if (mutation == "owner") BitConverter.GetBytes(101).CopyTo(bytes, 0);
            if (mutation == "footer") bytes[^1] ^= 1;
            if (mutation == "size") BitConverter.GetBytes(size - 1).CopyTo(bytes, 8);
            if (mutation == "class") BitConverter.GetBytes((ushort)1794).CopyTo(bytes, 12);
            return (bytes, offsets);
        }
        void Default(TfBlockDeserializer.TfField field)
        {
            if (field.Mods >> 4 == 5) { w.Write(0u); return; }
            int count = field.Mods >> 4 == 1 ? field.Cnt!.Value : 1;
            for (int i = 0; i < count; i++) switch (field.Type)
            {
                case 1: case 2: case 3: w.Write((byte)0); break;
                case 4: case 5: case 6: w.Write(0); break;
                case 7: case 10: w.Write(0L); break;
                case 8: w.Write(0u); break;
                case 9: w.Write(new byte[16]); break;
                case 13: w.Write(new byte[(field.Cnt ?? 1) * 8]); break;
                case 14 when (field.Mods & 15) == 0 && field.Ref is not null and not 20:
                    foreach (var nested in TfBlockDeserializer.SerializationFields(field.Ref.Value)!) Default(nested); break;
                case 14: w.Write(field.Ref == 20 ? -1 : 0); break;
                default: throw new InvalidOperationException(field.Name);
            }
        }
    }
}
