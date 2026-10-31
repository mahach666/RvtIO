using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The one schema model (TfSchemaReader -> SchemaDocument, with FileSchema / FormatsSchema as views, the shipped Revit 2022
/// table as a document with the legacy references) and the class-name keyed identity (TfClassNames, Element.RootClassName).</summary>
public class SchemaModelTests
{
    private static byte[] TwoClasses()
    {
        var w = new MemoryStream(); var bw = new BinaryWriter(w);
        void Text(string s) { bw.Write((ushort)s.Length); bw.Write(System.Text.Encoding.ASCII.GetBytes(s)); }
        // A(12) { i32 m_x }, B(13) : A { -> A m_a }, each with an empty GUID list
        bw.Write((ushort)0); Text("A"); bw.Write((ushort)0); bw.Write(1u); bw.Write(1u);
        bw.Write(3u); bw.Write("m_x"u8.ToArray()); bw.Write((byte)4); bw.Write((byte)0); bw.Write((ushort)0); bw.Write(0u);
        bw.Write((ushort)0); Text("B"); bw.Write((ushort)12); bw.Write(2u); bw.Write(1u);
        bw.Write(3u); bw.Write("m_a"u8.ToArray()); bw.Write((byte)0x0E); bw.Write((byte)0); bw.Write((ushort)0); bw.Write((ushort)12); bw.Write(0u);
        bw.Write(new byte[8]);
        return w.ToArray();
    }

    [Fact]
    public void FileSchemaAndFormatsSchemaAreViewsOfOneParse()
    {
        byte[] stream = TwoClasses();
        var document = TfSchemaReader.TryParse(stream)!;
        var file = FileSchema.Parse(stream)!;
        var formats = FormatsSchema.TryParse(stream)!;
        Assert.Equal(document.Classes.Select(c => (c.Id, c.Name, c.Base)), file.Classes.Values.Select(c => (c.Id, c.Name, c.Base)));
        Assert.Equal(document.Classes.Select(c => (c.Id, c.Name, c.Base)), formats.Classes.Values.Select(c => (c.Id, c.Name, c.Base)));
        Assert.Equal(4, file.FixedSize(12));
        Assert.Equal(4, file.FixedSize(13));            // B declares one class reference to A (4 bytes); the base fields are not summed
        Assert.True(file.IsSubclassOf(13, 12));
        Assert.Equal(12, formats.Get("B")!.Base);
        Assert.Equal(new FormatsSchema.Field("m_a", 0x0E, 0, 12, null), formats.Get("B")!.Fields[0]);
        Assert.Equal(new FileSchema.Field("m_a", 0x0E, 0, 12), file.ByName("B")!.Fields[0]);
        Assert.Equal(["B", "A"], file.ChainOf("B"));
        Assert.Empty(file.ChainOf("Nope"));
    }

    [Fact]
    public void ViewsRejectWhatTheOneParserRejects_AndTheOlderLayoutIsNotAcceptedBehindTheirBack()
    {
        byte[] good = TwoClasses();
        foreach (var bad in new[] { good[..^9], good[..3], [], [1, 0, 0, 0] })
        {
            Assert.Null(FileSchema.Parse(bad));
            Assert.Null(FormatsSchema.TryParse(bad));
            Assert.Null(TfSchemaReader.TryParse(bad));
        }
        Assert.NotNull(TfSchemaReader.Parse(good));     // the tool entry point still tries both layouts
    }

    [SkippableFact]
    public void Shipped2022Table_IsTheExactParseExceptForTheLegacyReferences()
    {
        var path = TestFiles.FindRelative("2022/Test_2022.rvt");
        var exact = TfSchemaReader.Load(path!);
        using var resource = typeof(TfBlockDeserializer).Assembly.GetManifestResourceStream("RvtIO.Resources.r22_schemas.json")!;
        var shipped = TfSchemaReader.ReadShipped2022(resource);
        Assert.Equal(exact.Classes.Count, shipped.Classes.Count);
        int fields = 0, sameRef = 0;
        for (int i = 0; i < exact.Classes.Count; i++)
        {
            var e = exact.Classes[i]; var s = shipped.Classes[i];
            Assert.Equal((e.Id, e.Name, e.Base, e.Version), (s.Id, s.Name, s.Base, s.Version));
            Assert.Equal(e.Fields.Count, s.Fields.Count);
            for (int k = 0; k < e.Fields.Count; k++, fields++)
            {
                Assert.Equal((e.Fields[k].Name, e.Fields[k].Type, e.Fields[k].Mods, e.Fields[k].Fixed), (s.Fields[k].Name, s.Fields[k].Type, s.Fields[k].Mods, s.Fields[k].Fixed));
                if (e.Fields[k].Ref == s.Fields[k].Ref) sameRef++;
            }
        }
        Assert.Equal((11709, 10636), (fields, sameRef));   // 1,073 legacy references differ
        // the legacy value, kept for the 2022 decoders: std::pair<ElementId,int>.second is a plain int, the table gives it its own class id
        var pair = TfBlockDeserializer.Schemas[19];
        Assert.Equal("second", pair.Fields[1].Name);
        Assert.Equal(19, pair.Fields[1].Ref);
        Assert.Null(exact.Classes.Single(c => c.Id == 19).Fields[1].Ref);
    }

    [Fact]
    public void ClassNames_AreUniqueInThe2022Table_AndKeysAreTheOldLiterals()
    {
        foreach (var (id, schema) in TfBlockDeserializer.Schemas)
        {
            Assert.Equal(id, TfClassNames.IdOf(schema.Name));
            Assert.Equal(schema.Name, TfClassNames.NameOf(id));
        }
        Assert.Equal("1829:m_familyId", TfClassNames.Key("FamilySymbol", "m_familyId"));
        Assert.Equal("1794:m_refs#", TfClassNames.Key("FamilyInstance", "m_refs#"));
        Assert.Equal("699:dormant@0", TfClassNames.Key("ColumnAttachment", "dormant@0"));
        Assert.Throws<KeyNotFoundException>(() => TfClassNames.IdOf("NoSuchClass"));
        Assert.Null(TfClassNames.NameOf(1));
        Assert.True(TfClassNames.IsSubclassOf("FamilySymbol", "Symbol"));
        Assert.True(TfClassNames.IsSubclassOf("FamilySymbol", "FamilySymbol"));
        Assert.False(TfClassNames.IsSubclassOf("SWall", "Symbol"));
        Assert.False(TfClassNames.IsSubclassOf("NoSuchClass", "Symbol"));
    }

    // the numeric root-class constants the Compact consumers used before they named classes, each against the name they now use
    [Theory]
    [InlineData(2486, "MaterialElem")]
    [InlineData(3524, "SWall")]
    [InlineData(3553, "SegmentCenterLine")]
    [InlineData(106, "Symbol")]
    [InlineData(1773, "FamilyBase")]
    [InlineData(1829, "FamilySymbol")]
    [InlineData(1794, "FamilyInstance")]
    public void PortedRootClassConstants_NameTheSameClass(int id, string name)
    {
        Assert.Equal(name, TfClassNames.NameOf(id));
        Assert.Equal(id, TfClassNames.IdOf(name));
    }

    [Fact]
    public void BoundCategoryClasses_NamesAreTheOldIds()
    {
        (int Id, string Name)[] old =
        [
        (110, "AbsDuctType"), (114, "AbsFlexDuctType"), (267, "AnalyticalModelBeam"), (273, "AnalyticalModelColumn"), (280,
        "AnalyticalModelIsolatedFoundation"), (281, "AnalyticalModelSlab"), (284, "AnalyticalModelWall"), (285,
        "AnalyticalModelWallFoundation"), (380, "AreaLoad"), (393, "AreaReinforcementType"), (512, "BasicWallType"), (646,
        "CompoundCeilingType"), (804, "ContFooting"), (813, "ContFootingType"), (873, "CorniceAttr"), (894,
        "CurtaSystemAttr"), (908, "CurtainRoofAttributes"), (986, "DBView3d"), (993, "DBViewAnonDrafting"), (995,
        "DBViewDrafting"), (997, "DBViewGraphSchedColumn"), (1005, "DBViewPlan"), (1011, "DBViewRendering"), (1016,
        "DBViewSection"), (1024, "DPart"), (1261, "EdgeSlabAttr"), (1837, "FasciaAttr"), (1860, "FilledRegion"), (1913,
        "Floor"), (1915, "FloorAttributes"), (2108, "Grid"), (2134, "GutterAttr"), (2180, "Hub"), (2264, "JoistSystem"),
        (2266, "JoistSystemAttr"), (2299, "Level"), (2320, "LineLoad"), (2447, "MassLevelData"), (2486, "MaterialElem"),
        (2559, "NewCurtainWallType"), (3061, "ProjectInfo"), (3069, "PropertyAttr"), (3114, "RampAttributes"), (3181,
        "RbsFlexPipeType"), (3227, "RbsPipeType"), (3272, "RbsWireType"), (3277, "Rebar"), (3281, "RebarBarType"), (3309,
        "RebarInSystem"), (3328, "RebarShape"), (3366, "RebarSystem"), (3452, "RoofAttributes"), (3471, "RoomElem"), (3545,
        "Section"), (3669, "StairsAttributes"), (3681, "StairsElement"), (3705, "StairsRailingAttr"), (3708, "StairsRun"),
        (3727, "StairsSupport"), (3742, "StairsType"), (3910, "SysMullionFamSym"), (3912, "SysPanelFamSym"), (3966,
        "Text3dAttrSymbol"), (4215, "ZoneElement"), (361, "ArcWall"), (642, "Ceiling"), (1322, "ElementGroup"), (1333,
        "ElementGroupType"), (3054, "ProfileRoof"), (485, "BaseRailing"), (3148, "RbsDuctCurve"), (3451, "Roof"), (3502,
        "RvtLinkInstance"), (3509, "RvtLinkSymbol"), (3568, "ShaftOpening"), (4157, "WallSweep")
        ];
        Assert.Equal(76, old.Length);
        foreach (var (id, name) in old) Assert.Equal(id, TfClassNames.IdOf(name));
    }

    [Fact]
    public void RootClassName_IsFilledForEveryLayout()
    {
        var r22 = new Element { SerializedOwnElementClassId = 1829 };
        Assert.Equal("FamilySymbol", r22.RootClassName);
        Assert.True(r22.RootClassIs("Symbol"));
        Assert.False(r22.RootClassIs("FamilyBase"));
        Assert.Null(new Element().RootClassName);
        Assert.False(new Element().RootClassIs("Symbol"));
        var wide = new Element { NativeRootClassName = "Foo", NativeRootClassChain = ["Foo", "Symbol", "Element"] };
        Assert.Equal("Foo", wide.RootClassName);
        Assert.True(wide.RootClassIs("Symbol"));
        Assert.False(wide.RootClassIs("FamilyBase"));
        Assert.False(new Element { NativeRootClassName = "Foo" }.RootClassIs("Symbol"));
        // the property rules see the class of every layout (their unvalidated cases are guarded where they are decided)
        Assert.Equal("FamilySymbol", r22.RuleClassName);
        Assert.True(r22.RuleClassIs("Symbol"));
        Assert.Equal("Foo", wide.RuleClassName);
        Assert.True(wide.RuleClassIs("Symbol"));
        Assert.False(wide.RuleClassIs("FamilyBase"));
    }
}
