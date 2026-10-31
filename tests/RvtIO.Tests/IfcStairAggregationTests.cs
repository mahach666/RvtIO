using RvtIO.Ifc;
using RvtIO.Internal;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

public class IfcStairAggregationTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void CompleteExportDoesNotDependOnParentChildEnumerationOrder(bool childFirst, bool authoredName)
    {
        var parent = IfcExporterSlabCommonPropertySetTests.Slab(100, 101, "Authored stair", -2000120, "OST_Stairs", null);
        var child = IfcExporterSlabCommonPropertySetTests.Slab(200, 201, "Authored run", -2000919, "OST_StairsRuns", null, 20);
        parent.SerializedOwnElementClassId = 3681; parent.SerializedStairCore = [new(200, 3708)];
        parent.SerializedStairTypeId = 101; parent.SerializedStairTypeLinkIsResolved = true;
        parent.ValidatedStairOccurrenceName = "Лестница"; parent.SerializedStairNameParameters = [];
        if (authoredName)
        {
            parent.SerializedStairNameParameters.Add(new() { Definition = new() { Name = "IfcName" }, StorageType = StorageType.String, Value = "first author" });
            parent.SerializedStairNameParameters.Add(new() { Definition = new() { Name = "NameOverride" }, StorageType = StorageType.String, Value = "final author" });
        }
        var parentType = new Element { Id = 101, Name = "Authored type", CategoryId = -2000120,
            SerializedStairsTypeClassId = 3742, ValidatedSystemFamilyName = "Монолитная лестница" };
        child.SerializedOwnElementClassId = 3708; child.SerializedOwningElementId = 100;
        child.SerializedStairNameParameters = [];
        child.SerializedStairRunInput = new(200, 100, Parsing.TfBlockDeserializer.StairRunStyle.Straight, [new(1, 0, 0)]);
        var info = new RevitFileInfo(); info.Elements.AddRange(childFirst ? [child, parent] : [parent, child]);
        info.Elements.Add(parentType);
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-stair-core-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter() { Profile = IfcProfile.Legacy }.Export(info, path);
            string text = File.ReadAllText(path);
            var products = System.Text.RegularExpressions.Regex.Matches(text, @"#(\d+)=(IFCSTAIR|IFCSTAIRFLIGHT)\(([^\r\n]*)\);");
            var stair = Assert.Single(products, match => match.Groups[2].Value == "IFCSTAIR");
            var run = Assert.Single(products, match => match.Groups[2].Value == "IFCSTAIRFLIGHT");
            Assert.Equal("$", IfcExporter.SplitStepArguments(stair.Groups[3].Value)[6]);
            string nativeName = authoredName ? "final author" : "Монолитная лестница:Лестница:100";
            Assert.Equal(StepWriter.Str(nativeName), IfcExporter.SplitStepArguments(stair.Groups[3].Value)[2]);
            Assert.Equal(StepWriter.Str(nativeName + " Run 1"), IfcExporter.SplitStepArguments(run.Groups[3].Value)[2]);
            Assert.Equal(".STRAIGHT_RUN_STAIR.", IfcExporter.SplitStepArguments(stair.Groups[3].Value)[8]);
            Assert.Equal("$", IfcExporter.SplitStepArguments(run.Groups[3].Value)[11]);
            Assert.StartsWith("#", IfcExporter.SplitStepArguments(run.Groups[3].Value)[6]);
            string stairRef = "#" + stair.Groups[1].Value, runRef = "#" + run.Groups[1].Value;
            Assert.Contains($",{stairRef},({runRef}));", text);
            foreach (System.Text.RegularExpressions.Match relation in System.Text.RegularExpressions.Regex.Matches(text, @"IFCRELCONTAINEDINSPATIALSTRUCTURE\(([^\r\n]*)\);"))
            {
                string contained = IfcExporter.SplitStepArguments(relation.Groups[1].Value)[4];
                Assert.Contains(stairRef, contained);
                Assert.DoesNotContain(runRef, IfcExporter.SplitStepArguments(contained[1..^1]));
            }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(3708, "IFCSTAIRFLIGHT")]
    [InlineData(3686, "IFCSLAB")]
    [InlineData(3727, "IFCMEMBER")]
    public void EmittedBodyCreatesCoreEdgeAndRemovesOnlyDirectChildContainment(ushort cls, string ifcClass)
    {
        var (writer, elements, products, contents) = Fixture(cls, ifcClass);
        string oldChild = writer.EntityAt(4).Args;
        StairAggregation.Apply(writer, elements, products, contents, 99);
        Assert.Equal(oldChild, writer.EntityAt(4).Args);
        Assert.Equal("$", IfcExporter.SplitStepArguments(writer.EntityAt(3).Args)[6]);
        var rel = writer.EntityAt(5); Assert.Equal("IFCRELAGGREGATES", rel.Type);
        string guid = IfcGuid.FromMd5("IfcRelAggregates:" + IfcGuid.FromElementId(100));
        Assert.Equal($"'{guid}',#99,$,$,#4,(#5)", rel.Args);
        Assert.Equal(new[] { 4, 999 }, contents[1]); Assert.Equal(new[] { 888 }, contents[2]);
    }

    [Theory]
    [InlineData("no_core")] [InlineData("no_parent")] [InlineData("no_child")]
    [InlineData("child_class")] [InlineData("parent_class")] [InlineData("child_body")]
    [InlineData("empty_body")] [InlineData("axis_only")] [InlineData("dangling_body")]
    [InlineData("wrong_owner")] [InlineData("wrong_native_class")]
    [InlineData("duplicate_child")] [InlineData("duplicate_parent")] [InlineData("duplicate_member")]
    [InlineData("wrong_tag")] [InlineData("non_core_class")] [InlineData("block_only")]
    public void RejectedSourceOrWriterOutcomeLeavesParentAndContainmentUnchanged(string mode)
    {
        var (writer, elements, products, contents) = Fixture();
        switch (mode)
        {
            case "no_core": elements[0].SerializedStairCore = null; break;
            case "no_parent": products.Remove(100); break;
            case "no_child": products.Remove(200); break;
            case "child_class": products[200] = (5, 0, "IFCBUILDINGELEMENTPROXY"); break;
            case "parent_class": products[100] = (4, 0, "IFCRAMP"); break;
            case "child_body": writer.ReplaceArgs(4, writer.EntityAt(4).Args.Replace("#3", "$")); break;
            case "empty_body": writer.ReplaceArgs(1, "#1,'Body','Brep',()"); break;
            case "axis_only": writer.ReplaceArgs(1, "#1,'Axis','Brep',(#1)"); break;
            case "dangling_body": writer.ReplaceArgs(1, "#1,'Body','Brep',(#999999)"); break;
            case "wrong_owner": elements[1].SerializedOwningElementId = 999; break;
            case "wrong_native_class": elements[1].SerializedOwnElementClassId = 3739; break;
            case "duplicate_child": elements.Add(new() { Id = 200 }); break;
            case "duplicate_parent": elements.Add(new() { Id = 100 }); break;
            case "duplicate_member": elements[0].SerializedStairCore = [new(200, 3708), new(200, 3708)]; break;
            case "wrong_tag": writer.ReplaceArgs(4, writer.EntityAt(4).Args.Replace("'200'", "'201'")); break;
            case "non_core_class": elements[0].SerializedStairCore = [new(200, 3739)]; break;
            case "block_only": elements[1].IsBlockOnly = true; break;
        }
        string oldParent = writer.EntityAt(3).Args;
        StairAggregation.Apply(writer, elements, products, contents, 99);
        Assert.Equal(5, writer.EntityCount); Assert.Equal(oldParent, writer.EntityAt(3).Args);
        Assert.Equal(new[] { 4, 5, 999 }, contents[1]); Assert.Equal(new[] { 5, 888 }, contents[2]);
    }

    [Fact]
    public void AFilteredSiblingDoesNotInvalidateTheSuccessfulComponent()
    {
        var (writer, elements, products, contents) = Fixture();
        elements[0].SerializedStairCore = [new(200, 3708), new(201, 3686)];
        elements.Add(new() { Id = 201, CategoryId = -2000920, SerializedOwnElementClassId = 3686, SerializedOwningElementId = 100 });
        StairAggregation.Apply(writer, elements, products, contents, 99);
        Assert.Equal("IFCRELAGGREGATES", writer.EntityAt(5).Type);
        Assert.EndsWith(",(#5)", writer.EntityAt(5).Args);
    }

    [Fact]
    public void ExistingDecompositionIsNeverDuplicatedOrReassigned()
    {
        var (writer, elements, products, contents) = Fixture();
        writer.Add("IFCRELAGGREGATES", "'existing',#99,$,$,#777,(#5)");
        StairAggregation.Apply(writer, elements, products, contents, 99);
        Assert.Equal(6, writer.EntityCount); Assert.NotEqual("$", IfcExporter.SplitStepArguments(writer.EntityAt(3).Args)[6]);
    }

    [Fact]
    public void RepeatedApplicationIsIdempotent()
    {
        var (writer, elements, products, contents) = Fixture();
        StairAggregation.Apply(writer, elements, products, contents, 99);
        StairAggregation.Apply(writer, elements, products, contents, 99);
        Assert.Equal(6, writer.EntityCount);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void RailingUsesOwnHostNotRegisteredListOrElemRecOwner(bool emittedBody)
    {
        var (writer, elements, products, contents) = Fixture();
        elements.Add(new() { Id = 300, SerializedRailingStairHostId = 100, SerializedOwningElementId = -1, SerializedOwnElementClassId = 485 });
        int railing = writer.Add("IFCRAILING", $"'{IfcGuid.FromElementId(300)}',#99,'rail',$,'type',#999,{(emittedBody ? "#3" : "$" )},'300',.NOTDEFINED.");
        products[300] = (railing, 0, "IFCRAILING"); contents[1].Add(railing);
        StairAggregation.Apply(writer, elements, products, contents, 99);
        Assert.EndsWith(emittedBody ? ",(#5,#6)" : ",(#5)", writer.EntityAt(6).Args);
        Assert.Equal(!emittedBody, contents[1].Contains(railing));
    }

    [Fact]
    public void LegacyStairHoldsNoBodyAndAggregatesASecondStairWithItAndItsRailings()
    {
        var (writer, elements, products, contents) = LegacyFixture();
        var bodies = StairAggregation.SplitLegacyBodies(writer, elements, products, contents, 99, 98, new HashSet<Guid>());
        int body = Assert.Single(bodies).Value;
        var parentArgs = IfcExporter.SplitStepArguments(writer.EntityAt(3).Args);
        Assert.Equal("$", parentArgs[6]);
        var bodyArgs = IfcExporter.SplitStepArguments(writer.EntityAt(body - 1).Args);
        Assert.Equal("#3", bodyArgs[6]);
        Assert.Equal(parentArgs[2], bodyArgs[2]); Assert.Equal(parentArgs[7], bodyArgs[7]);
        Assert.NotEqual(parentArgs[0], bodyArgs[0]);
        var placement = writer.EntityAt(int.Parse(bodyArgs[5][1..]) - 1);
        Assert.Equal("IFCLOCALPLACEMENT", placement.Type); Assert.Equal("#999,#98", placement.Args);
        var rel = writer.EntityAt(writer.EntityCount - 1); Assert.Equal("IFCRELAGGREGATES", rel.Type);
        Assert.EndsWith($",#4,(#{body},#5)", rel.Args);
        Assert.Equal(new[] { 4, 999 }, contents[1]);
    }

    [Theory]
    [InlineData("Stairs", true, "Stair:Massiv")]
    [InlineData("Stairs", false, "OST_Stairs:Massiv")]   // the older release keeps the category name
    [InlineData("StairsElement", true, "OST_Stairs:Massiv")]
    public void LegacyStairIsNamedAfterTheStairResource(string rootClass, bool currentProfile, string expected)
    {
        var stair = new Element { Id = 100, Name = "Massiv", Category = "OST_Stairs", CategoryId = -2000120,
            NativeRootClassName = rootClass, ValidatedStairOccurrenceName = "Stair" };
        var byId = new Dictionary<int, Element> { [100] = stair };
        var counts = new Dictionary<int, int> { [100] = 1 };
        Assert.Equal(expected, IfcExporter.ResolveProductFamilyAndTypeName(stair, "IFCSTAIR", byId, counts, currentProfile));
    }

    [Theory]
    [InlineData("modern")] [InlineData("no_body")] [InlineData("block_only")] [InlineData("not_a_stair")]
    public void OnlyALegacyStairWithABodyIsSplit(string mode)
    {
        var (writer, elements, products, contents) = LegacyFixture();
        switch (mode)
        {
            case "modern": elements[0].NativeRootClassName = "StairsElement"; break;
            case "no_body": writer.ReplaceArgs(3, writer.EntityAt(3).Args.Replace("#3", "$")); break;
            case "block_only": elements[0].IsBlockOnly = true; break;
            case "not_a_stair": products[100] = (4, 0, "IFCRAMP"); break;
        }
        int count = writer.EntityCount;
        Assert.Empty(StairAggregation.SplitLegacyBodies(writer, elements, products, contents, 99, 98, new HashSet<Guid>()));
        Assert.Equal(count, writer.EntityCount);
    }

    private static (StepWriter, List<Element>, Dictionary<int, (int entityId, int placementId, string ifcType)>, Dictionary<int, List<int>>)
        LegacyFixture()
    {
        var writer = new StepWriter();
        writer.Add("IFCFACETEDBREP", "#999");
        writer.Add("IFCSHAPEREPRESENTATION", "#999,'Body','Brep',(#1)");
        writer.Add("IFCPRODUCTDEFINITIONSHAPE", "$,$,(#2)");
        writer.Add("IFCSTAIR", $"'{IfcGuid.FromElementId(100)}',#99,'Stair:type:100',$,'Stair:type',#999,#3,'100',.NOTDEFINED.");
        writer.Add("IFCRAILING", $"'{IfcGuid.FromElementId(300)}',#99,'rail',$,'type',#999,#3,'300',$");
        List<Element> elements = [new() { Id = 100, CategoryId = -2000120, NativeRootClassName = "Stairs" },
            new() { Id = 300, SerializedRailingStairHostId = 100, SerializedOwningElementId = -1, SerializedOwnElementClassId = 485, NativeRootClassName = "BaseRailing" }];
        Dictionary<int, (int, int, string)> products = new() { [100] = (4, 0, "IFCSTAIR"), [300] = (5, 0, "IFCRAILING") };
        Dictionary<int, List<int>> contents = new() { [1] = [4, 5, 999] };
        return (writer, elements, products, contents);
    }

    private static (StepWriter, List<Element>, Dictionary<int, (int entityId, int placementId, string ifcType)>, Dictionary<int, List<int>>)
        Fixture(ushort cls = 3708, string ifcClass = "IFCSTAIRFLIGHT")
    {
        var writer = new StepWriter();
        writer.Add("IFCFACETEDBREP", "#999");
        writer.Add("IFCSHAPEREPRESENTATION", "#999,'Body','Brep',(#1)");
        writer.Add("IFCPRODUCTDEFINITIONSHAPE", "$,$,(#2)");
        writer.Add("IFCSTAIR", $"'{IfcGuid.FromElementId(100)}',#99,'parent',$,'type',#999,#3,'100',.NOTDEFINED.");
        writer.Add(ifcClass, $"'{IfcGuid.FromElementId(200)}',#99,'child',$,'type',#999,#3,'200'");
        List<Element> elements = [new() { Id = 100, CategoryId = -2000120, SerializedOwnElementClassId = 3681, SerializedStairCore = [new(200, cls)] },
            new() { Id = 200, CategoryId = -2000151, SerializedOwnElementClassId = cls, SerializedOwningElementId = 100 }];
        Dictionary<int, (int, int, string)> products = new() { [100] = (4, 0, "IFCSTAIR"), [200] = (5, 0, ifcClass) };
        Dictionary<int, List<int>> contents = new() { [1] = [4, 5, 999], [2] = [5, 888] };
        return (writer, elements, products, contents);
    }
}
