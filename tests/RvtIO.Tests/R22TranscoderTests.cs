using System.Text;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The rewrite of a later release's partition stream into the Revit 2022 layout (<see cref="R22Transcoder"/>): a hand-made block
/// with a renumbered class, a 64-bit id, a field only the file has, a field only the 2022 class has and an object of a class the 2022
/// table does not know, then Test_2025.rvt read with the rewrite.</summary>
public class R22TranscoderTests
{
    private static SchemaField Field(string name, byte type, byte mods = 0, int? reference = null) =>
        new(name, type, mods, 0, null, reference, null, null);

    // the file: Holder(5) is renumbered to 11, Store(3) to 10 and Extra(4) has no counterpart
    private static SchemaDocument Source() => new()
    {
        Classes =
        [
            new(1, "Identifier", 0, 2, [Field("m_id64", 11)], []),
            new(2, "ElementId", 0, 1, [Field("m_id", 14, 0, 1)], []),
            new(3, "Store", 0, 1, [Field("m_value", 4)], []),
            new(4, "Extra", 0, 1, [Field("m_x", 4)], []),
            new(5, "Holder", 0, 1,
                [Field("m_pStore", 14, 1), Field("m_items", 14, 0x51), Field("m_id", 14, 0, 2), Field("m_added", 4), Field("m_name", 8, 96)], []),
            new(6, "Pair", 0, 1, [Field("m_first", 4), Field("m_second", 4), Field("m_id", 14, 0, 2)], []),
        ],
    };

    private static SchemaDocument Target() => new()
    {
        Classes =
        [
            new(1, "Identifier", 0, 1, [Field("m_id", 4)], []),
            new(2, "ElementId", 0, 1, [Field("m_id", 14, 0, 1)], []),
            new(10, "Store", 0, 1, [Field("m_value", 4)], []),
            new(11, "Holder", 0, 1,
                [Field("m_pStore", 14, 1), Field("m_items", 14, 0x51), Field("m_id", 14, 0, 2), Field("m_gone", 4), Field("m_name", 8, 96)], []),
            new(12, "Pair", 0, 1, [Field("m_id", 14, 0, 2), Field("m_second", 4), Field("m_first", 4)], []),
        ],
    };

    private static byte[] Bytes(Action<BinaryWriter> body)
    {
        var stream = new MemoryStream();
        body(new BinaryWriter(stream, Encoding.Unicode));
        return stream.ToArray();
    }

    private static void Owned(BinaryWriter w, int tag) { w.Write(0xFFFFFFFFu); w.Write((ushort)tag); }

    [Fact]
    public void Payload_RenumbersCutsInsertsAndLeavesOutUnknownObjects()
    {
        var block = Bytes(w =>
        {
            w.Write((ushort)5);
            Owned(w, 3);                                   // m_pStore
            w.Write(2u); Owned(w, 4); Owned(w, 3);         // m_items: an Extra and a Store
            w.Write(77L);                                  // m_id (64 bits)
            w.Write(5);                                    // m_added
            w.Write(2u); w.Write(Encoding.Unicode.GetBytes("ab"));
            w.Write(42);                                   // Store
            w.Write(9);                                    // Extra
            w.Write(43);                                   // Store
        });
        var expected = Bytes(w =>
        {
            w.Write((ushort)11);
            Owned(w, 10);
            w.Write(1u); Owned(w, 10);                     // the Extra left the array, the count follows
            w.Write(77);                                   // m_id (32 bits)
            w.Write(0);                                    // m_gone, empty
            w.Write(2u); w.Write(Encoding.Unicode.GetBytes("ab"));
            w.Write(42);
            w.Write(43);
        });
        var transcoder = new R22Transcoder(Source(), Target(), 8);
        var result = transcoder.Payload(block, 0, block.Length);
        Assert.Equal(expected, result);
        var walk = new WideBlockWalker(Target(), (_, _) => null, []).Run(result!, 0, result!.Length, 11);
        Assert.True(walk.Exact, walk.Error);
    }

    [Fact]
    public void Payload_WritesFieldsInTheOrderOfThe2022Class()
    {
        var block = Bytes(w => { w.Write((ushort)6); w.Write(1); w.Write(2); w.Write(77L); });
        var expected = Bytes(w => { w.Write((ushort)12); w.Write(77); w.Write(2); w.Write(1); });
        Assert.Equal(expected, new R22Transcoder(Source(), Target(), 8).Payload(block, 0, block.Length));
    }

    [Fact]
    public void Payload_RootClassWithoutCounterpartIsNotRewritten()
    {
        var block = Bytes(w => { w.Write((ushort)4); w.Write(1); });
        Assert.Null(new R22Transcoder(Source(), Target(), 8).Payload(block, 0, block.Length));
    }

    [Fact]
    public void Payload_IdBeyond32BitsIsNotRewritten()
    {
        var block = Bytes(w =>
        {
            w.Write((ushort)5); w.Write(0u); w.Write(0u);
            w.Write(1L << 40); w.Write(5); w.Write(0u);
        });
        Assert.Null(new R22Transcoder(Source(), Target(), 8).Payload(block, 0, block.Length));
    }

    [SkippableFact]
    public void Test2025_ReadWithTheRewriteSeesBlocksAndGeometryOfTheRevit2022Readers()
    {
        var diag = new StringWriter();
        var reader = new RvtFileReader(new RvtReaderOptions { R22Layout = true, DiagnosticOutput = diag });
        var info = reader.Read(TestFileFixture.FindTestFile("Test_2025.rvt"));
        var state = reader.LastParsingState!;
        Assert.True(state.R22View);
        Assert.Contains("Revit 2022 layout:", diag.ToString());
        Assert.NotEmpty(state.GrepBlocks);
        Assert.True(state.Records.Count > state.ElemTable.Count / 2);
        Assert.Contains(info.Elements, e => e.Geometry?.Faces.Count > 0);
    }

    [Fact]
    public void DriveCurve_OnlyTheCurveKeysOfTheRewrittenBlockAreAddedAndNothingIsOverwritten()
    {
        var target = new TfBlockDeserializer.NativeParams();
        target.DoubleFields["87:m_pCrv.endParams@1"] = 5.0;
        target.DoubleFields["1709:m_rotation"] = 0.25;
        var tail = new TfBlockDeserializer.NativeParams();
        tail.DoubleFields["87:m_pCrv.dirVec@0"] = 1.0;
        tail.DoubleFields["87:m_pCrv.endParams@1"] = 9.0;
        tail.DoubleFields["1709:m_rotation"] = 3.0;
        RvtFileReader.AddDriveCurveFromRewrittenBlock(target, tail);
        Assert.Equal(1.0, target.DoubleFields["87:m_pCrv.dirVec@0"]);
        Assert.Equal(5.0, target.DoubleFields["87:m_pCrv.endParams@1"]);
        Assert.Equal(0.25, target.DoubleFields["1709:m_rotation"]);
        RvtFileReader.AddDriveCurveFromRewrittenBlock(target, null);
        Assert.Equal(3, target.DoubleFields.Count);
    }

    [SkippableFact]
    public void Test2025_WithoutTheRewriteTheSchemaPathAloneApplies()
    {
        var reader = new RvtFileReader(new RvtReaderOptions());
        reader.Read(TestFileFixture.FindTestFile("Test_2025.rvt"));
        Assert.False(reader.LastParsingState!.R22View);
    }
}
