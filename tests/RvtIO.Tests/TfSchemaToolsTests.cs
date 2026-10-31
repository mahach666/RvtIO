using System.Text;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class TfSchemaToolsTests
{

    private static void U16(List<byte> b, int v) { b.Add((byte)v); b.Add((byte)(v >> 8)); }
    private static void U32(List<byte> b, int v) { U16(b, v & 0xFFFF); U16(b, v >> 16); }
    private static void Name16(List<byte> b, string s) { U16(b, s.Length); b.AddRange(Encoding.ASCII.GetBytes(s)); }
    private static void Field(List<byte> b, string name, int type, int mods, int? fixedCount = null, int? typeRef = null)
    {
        U32(b, name.Length); b.AddRange(Encoding.ASCII.GetBytes(name));
        b.Add((byte)type); b.Add((byte)mods); U16(b, 0);
        if (fixedCount is { } c) U32(b, c);
        if (typeRef is { } r) U16(b, r);
    }

    /// <summary>Alpha(12) with a double[3], Beta(13) deriving from Alpha with a pointer-free class reference to Alpha.</summary>
    private static byte[] Stream(bool guidLists, int betaInlineId = 15)
    {
        var b = new List<byte>();
        U16(b, 0); Name16(b, "Alpha"); U16(b, 0); U32(b, 3); U32(b, 1);
        Field(b, "m_xyz", 7, 0x10, fixedCount: 3);
        if (guidLists) { U32(b, 1); b.AddRange(Enumerable.Range(1, 16).Select(i => (byte)i)); }
        U16(b, 0); Name16(b, "Beta"); U16(b, 12); U32(b, 1); U32(b, 1);
        Field(b, "m_alpha", 14, 0, typeRef: 12);
        if (guidLists) U32(b, 0);
        U16(b, 0); Name16(b, "Gamma"); U16(b, 0x8000 | betaInlineId); U16(b, 0); Name16(b, "Inline");
        U16(b, 0); U32(b, 0); U32(b, 0); if (guidLists) U32(b, 0);
        U32(b, 0); U32(b, 0); if (guidLists) U32(b, 0);
        b.AddRange(new byte[8]);
        return [.. b];
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BothStreamLayoutsParse_WithImplicitIdsFrom12(bool guidLists)
    {
        var doc = TfSchemaReader.Parse(Stream(guidLists));
        Assert.Equal(guidLists, doc.GuidLists);
        Assert.Equal(["Alpha", "Beta", "Gamma", "Inline"], doc.Classes.Select(c => c.Name));
        Assert.Equal([12, 13, 14, 15], doc.Classes.Select(c => c.Id));
        // Gamma is registered before its inline base, so the base takes the next id.
        Assert.Equal(15, doc.Classes[2].Base);
        var alpha = doc.Classes[0];
        Assert.Equal(3, alpha.Version);
        Assert.Equal(new SchemaField("m_xyz", 7, 0x10, 0, 3, null, null, null), alpha.Fields[0]);
        Assert.Equal(12, doc.Classes[1].Fields[0].Ref);
        Assert.Equal(guidLists ? 1 : 0, alpha.Guids.Count);
        Assert.Empty(TfSchemaTools.Validate(doc));
    }

    [Fact]
    public void InlineIdThatIsNotTheCounter_IsRejected()
    {
        var e = Assert.Throws<FormatException>(() => TfSchemaReader.Parse(Stream(true, betaInlineId: 20)));
        Assert.Contains("inline id 20", e.Message);
    }

    [Fact]
    public void Json_RoundTripsEveryField()
    {
        var doc = TfSchemaReader.Parse(Stream(true));
        var again = TfSchemaReader.ReadJson(TfSchemaReader.WriteJson(doc));
        Assert.Equal(doc.StreamSha256, again.StreamSha256);
        Assert.Equal(doc.Classes.Count, again.Classes.Count);
        for (int i = 0; i < doc.Classes.Count; i++)
        {
            Assert.Equal(doc.Classes[i].Name, again.Classes[i].Name);
            Assert.Equal(doc.Classes[i].Guids, again.Classes[i].Guids);
            Assert.Equal(doc.Classes[i].Fields, again.Classes[i].Fields);
        }
    }

    [Fact]
    public void Validate_ReportsInconsistenciesAndUnknownTypeCodes()
    {
        var doc = new SchemaDocument
        {
            Classes =
            [
                new(12, "A", 99, 0, [new("m_a", 7, 0x10, 0, null, null, null, null), new("m_a", 11, 0, 0, null, null, null, null)], []),
                new(14, "A", 12, 0, [new("m_r", 14, 0, 0, null, 77, null, null)], []),
            ],
        };
        var codes = TfSchemaTools.Validate(doc).Select(i => i.Code).ToHashSet();
        foreach (var expected in new[] { "base-unresolved", "duplicate-name", "id-sequence", "fixed-count", "duplicate-field", "type-code", "ref-unresolved" })
            Assert.Contains(expected, codes);
    }

    [Fact]
    public void Validate_FindsABaseCycle()
    {
        var doc = new SchemaDocument { Classes = [new(12, "A", 13, 0, [], []), new(13, "B", 12, 0, [], [])] };
        Assert.Contains(TfSchemaTools.Validate(doc), i => i.Code == "base-cycle");
    }

    [Fact]
    public void Diff_MatchesByName_AndReportsEachKind()
    {
        var a = TfSchemaReader.Parse(Stream(true));
        var b = TfSchemaReader.ReadJson(TfSchemaReader.WriteJson(a));
        Assert.Empty(TfSchemaTools.Diff(a, b));
        // Ids shift, a field is added, one retyped, Gamma is dropped, Delta appears.
        b.Classes = [.. b.Classes.Where(c => c.Name != "Gamma").Select(c => c with { Id = c.Id + 1 })];
        b.Classes[0] = b.Classes[0] with { Fields = [.. b.Classes[0].Fields, new("m_new", 4, 0, 0, null, null, null, null)] };
        b.Classes[1] = b.Classes[1] with { Fields = [b.Classes[1].Fields[0] with { Mods = 1 }], Base = 13 };
        b.Classes.Add(new(99, "Delta", 0, 5, [new("m_d", 4, 0, 0, null, null, null, null), new("m_e", 8, 0x60, 0, null, null, null, null)], []));
        var kinds = TfSchemaTools.Diff(a, b).Select(c => $"{c.Kind}:{c.Class}.{c.Field}").ToList();
        Assert.Contains("class-id-moved:Alpha.", kinds);
        Assert.Contains("field-added:Alpha.m_new", kinds);
        Assert.Contains("field-retyped:Beta.m_alpha", kinds);
        Assert.Contains("class-removed:Gamma.", kinds);
        Assert.Contains("class-added:Delta.", kinds);
    }

    // Real files: n of m against the Revit 2022 table the decoders are built on.

    [SkippableFact]
    public void Revit2022File_ReproducesTheShippedTableExactly()
    {
        var path = TestFiles.FindRelative("2022/Test_2022.rvt");
        var doc = TfSchemaReader.Load(path!);
        var table = TfBlockDeserializer.Schemas;
        Assert.Equal(table.Count, doc.Classes.Count);
        int fields = 0;
        foreach (var c in doc.Classes)
        {
            var t = table[c.Id];
            Assert.Equal(t.Name, c.Name);
            Assert.Equal(t.Base ?? 0, c.Base);
            Assert.Equal(t.Fields.Count, c.Fields.Count);
            for (int i = 0; i < c.Fields.Count; i++, fields++)
            {
                Assert.Equal(t.Fields[i].Name, c.Fields[i].Name);
                Assert.Equal(t.Fields[i].Type, c.Fields[i].Type);
                Assert.Equal(t.Fields[i].Mods, c.Fields[i].Mods);
            }
        }
        Assert.Equal((4307, 11709), (doc.Classes.Count, fields));
        Assert.DoesNotContain(TfSchemaTools.Validate(doc, TfBlockDeserializer.Schemas.Values.SelectMany(x => x.Fields).Select(f => (f.Type, f.Mods)).ToHashSet()), i => i.Severity != "info");
    }

    [SkippableTheory]
    [InlineData("2020/Test_2020.rvt", 28)]
    [InlineData("2022/Test_2022.rvt", 28)]
    [InlineData("2025/Test_2025.rvt", 40)]
    public void ElemRecSize_FollowsFromTheSchemaAndPredictsTheElemTableRows(string relative, int size)
    {
        var path = TestFiles.FindRelative(relative);
        var doc = TfSchemaReader.Load(path!);
        var ids = doc.ById;
        int rec = doc.Classes.Single(c => c.Name == "ElemRec").Id, table = doc.Classes.Single(c => c.Name == "ElemTable").Id;
        Assert.Equal(size, TfSchemaTools.FixedSize(ids, rec));
        using var root = OpenMcdf.RootStorage.OpenRead(path!);
        var data = PagedGzipStream.Read(root, "Global", "ElemTable")!;
        Assert.Equal(table, BitConverter.ToUInt16(data, 0));
        uint rows = BitConverter.ToUInt32(data, 2);
        Assert.True(6L + rows * size <= data.Length);
        // m_id is the key of the table: strictly increasing at the offset the schema gives it.
        int idAt = 0, idWidth = TfSchemaTools.FixedSize(ids, 20) ?? 0;
        foreach (var f in ids[rec].Fields)
        {
            if (f.Name == "m_id") break;
            idAt += f.Type == 14 ? TfSchemaTools.FixedSize(ids, f.Ref!.Value)!.Value : f.Type switch { 4 => 4, _ => 0 };
        }
        long previous = -1;
        for (int i = 0; i < rows; i++)
        {
            long id = idWidth == 8 ? BitConverter.ToInt64(data, 6 + i * size + idAt) : BitConverter.ToUInt32(data, 6 + i * size + idAt);
            Assert.True(id > previous);
            previous = id;
        }
    }

    [SkippableTheory]
    [InlineData("2020/Test_2020.rvt", 4164, 11481)]
    [InlineData("2025/Test_2025.rvt", 4600, 12296)]
    public void OtherVersions_ParseAndAreConsistent(string relative, int classes, int fields)
    {
        var path = TestFiles.FindRelative(relative);
        var doc = TfSchemaReader.Load(path!);
        Assert.Equal((classes, fields), (doc.Classes.Count, doc.FieldCount));
        Assert.DoesNotContain(TfSchemaTools.Validate(doc), i => i.Severity == "error");
    }
}
