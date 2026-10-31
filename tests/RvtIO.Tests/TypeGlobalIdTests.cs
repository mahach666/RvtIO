using System.Reflection;
using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

public class TypeGlobalIdTests
{
    private static readonly Guid SymbolExportId = Guid.Parse("1f34752d-926d-4ab8-8425-c6d81fe02dab");
    private static string NativeGuid => IfcGuid.ToIfcGuid(SymbolExportId);
    private static string RelationGuid => IfcGuid.FromMd5("IfcRelDefinesByType:" + NativeGuid);

    [Fact]
    public void AutodeskMd5Key_UsesDotNetGuidByteOrder_WithoutVersionBitRewrite()
    {
        Assert.Equal("0VD7KjacrAk8GbnjWVu2sh", NativeGuid);
        Assert.Equal("30ZtzdzSavbBcvvxXAo_DA", RelationGuid);
    }

    [Theory]
    [InlineData("IFCMEMBER")]
    [InlineData("IFCPLATE")]
    [InlineData("IFCCOLUMN")]
    [InlineData("IFCBUILDINGELEMENTPROXY")]
    [InlineData("IFCFURNISHINGELEMENT")]
    [InlineData("IFCFLOWTERMINAL")]
    [InlineData("IFCFLOWSEGMENT")]
    [InlineData("IFCSLAB")]
    [InlineData("IFCSTAIRFLIGHT")]
    public void MappedType_UsesOriginalSymbolExportId_NotTheDisplayType(string product)
    {
        var (writer, sources) = Mapped(product);
        int type = Assert.Single(sources).Key;
        Assert.Equal(NativeGuid, sources[type]);
        var before = Entities(writer);
        Assert.Equal((1, 1), Apply(writer, sources));
        var after = Entities(writer);
        Assert.Equal(before.Length, after.Length);
        for (int i = 0; i < before.Length; i++)
        {
            Assert.Equal(before[i].Id, after[i].Id);
            Assert.Equal(before[i].Type, after[i].Type);
            if (before[i].Id == type || before[i].Type == "IFCRELDEFINESBYTYPE")
                Assert.Equal(before[i].Args[24..], after[i].Args[24..]);
            else
                Assert.Equal(before[i].Args, after[i].Args);
        }
        Assert.StartsWith($"'{NativeGuid}',", Assert.Single(after, e => e.Id == type).Args);
        Assert.StartsWith($"'{RelationGuid}',", Assert.Single(after, e => e.Type == "IFCRELDEFINESBYTYPE").Args);
        Assert.Equal((0, 0), Apply(writer, sources)); // Idempotent.
    }

    [Theory]
    [InlineData("IFCDOOR")]
    [InlineData("IFCWINDOW")]
    [InlineData("IFCWALL")]
    [InlineData("IFCCURTAINWALL")]
    [InlineData("IFCBEAM")]
    [InlineData("IFCCOVERING")]
    [InlineData("IFCRAILING")]
    public void OtherExportPaths_DoNotBorrowAMappedSymbolGuid(string product)
    {
        var (writer, sources) = Mapped(product);
        Assert.Empty(sources);
        var before = Entities(writer);
        Assert.Equal((0, 0), Apply(writer, sources));
        Assert.Equal(before, Entities(writer));
    }

    [Theory]
    [InlineData(false, false, -1019000)]
    [InlineData(false, false, -1019001)]
    [InlineData(false, true, -1019001)]
    [InlineData(true, false, -1019001)]
    [InlineData(true, true, -1019001)]
    public void GuidParameterOnSymbolOrDisplayType_KeepsSyntheticIdentity(bool canonical, bool definitionOnly, int bip)
    {
        var (writer, sources) = Mapped("IFCBUILDINGELEMENTPROXY", configure: elements =>
            elements[canonical ? 500 : 601].Parameters.Add(new Parameter
            {
                Id = definitionOnly ? 0 : bip, Definition = new Definition { Id = bip },
                StorageType = StorageType.String, Value = "unread override",
            }));
        Assert.Empty(sources);
        var before = Entities(writer);
        Assert.Equal((0, 0), Apply(writer, sources));
        Assert.Equal(before, Entities(writer));
    }

    [Theory]
    [InlineData(0)] // missing export id
    [InlineData(1)] // no complete top-level mapped validation
    public void MissingSourceSource_DoesNotInventAnExactGuid(int scenario)
    {
        var (writer, sources) = Mapped("IFCBUILDINGELEMENTPROXY", configure: elements =>
        {
            if (scenario == 0) elements[601].SerializedExportId = null;
            else elements[1].Geometry!.ExactMappedInstances = null;
        });
        Assert.Empty(sources);
        Assert.Equal((0, 0), Apply(writer, sources));
    }

    [Fact]
    public void AlreadyExactSpaceType_GetsOnlyTheMd5Relationship()
    {
        var writer = new StepWriter();
        int type = writer.Add("IFCSPACETYPE", $"'{NativeGuid}',$,'Room',$,$,$,$,'123',$,.NOTDEFINED.");
        writer.Add("IFCRELDEFINESBYTYPE", $"'{IfcGuid.FromName("space-rel")}',900,$,$,(#1001),#{type}");
        Assert.Equal((0, 1), Apply(writer, new Dictionary<int, string> { [type] = NativeGuid }));
        Assert.StartsWith($"'{NativeGuid}',", writer.EntityAt(0).Args);
        Assert.StartsWith($"'{RelationGuid}',", writer.EntityAt(1).Args);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateDesiredGuid_NeverChoosesTheFirstGroup(bool reverse)
    {
        var writer = new StepWriter();
        var sources = new Dictionary<int, string>();
        foreach (int ordinal in reverse ? new[] { 2, 1 } : new[] { 1, 2 })
        {
            int type = writer.Add("IFCMEMBERTYPE", $"'{IfcGuid.FromName("old-type:" + ordinal)}',$,'Type',$,$,$,$,'500',$,.NOTDEFINED.");
            writer.Add("IFCRELDEFINESBYTYPE", $"'{IfcGuid.FromName("old-rel:" + ordinal)}',900,$,$,(#1001),#{type}");
            sources[type] = NativeGuid;
        }
        var before = Entities(writer);
        Assert.Equal((0, 0), Apply(writer, sources));
        Assert.Equal(before, Entities(writer));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingTypeOrRelationshipGuidCollision_IsAtomic(bool relationship)
    {
        var (writer, sources) = Mapped("IFCBUILDINGELEMENTPROXY");
        writer.Add("IFCWALL", $"'{(relationship ? RelationGuid : NativeGuid)}',$,'Other',$,$,$,$,'456'");
        var before = Entities(writer);
        Assert.Equal((0, 0), Apply(writer, sources));
        Assert.Equal(before, Entities(writer));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuidReleasedByAnotherRewrite_IsStillOccupiedInTheInitialSnapshot(bool reverse)
    {
        var writer = new StepWriter();
        var sources = new Dictionary<int, string>();
        var types = new Dictionary<int, int>();
        string otherGuid = IfcGuid.ToIfcGuid(Guid.Parse("baad37a2-42b0-458d-9a77-0d87300f75c2"));
        foreach (int ordinal in reverse ? new[] { 2, 1 } : new[] { 1, 2 })
        {
            int type = writer.Add("IFCMEMBERTYPE", $"'{(ordinal == 2 ? NativeGuid : IfcGuid.FromName("old-type:" + ordinal))}',$,'Type',$,$,$,$,'500',$,.NOTDEFINED.");
            writer.Add("IFCRELDEFINESBYTYPE", $"'{IfcGuid.FromName("old-rel:" + ordinal)}',900,$,$,(#1001),#{type}");
            types[ordinal] = type;
            sources[type] = ordinal == 1 ? NativeGuid : otherGuid;
        }
        var before = Entities(writer);
        Assert.Equal((1, 1), Apply(writer, sources));
        var after = Entities(writer);
        Assert.Equal(Assert.Single(before, e => e.Id == types[1]), Assert.Single(after, e => e.Id == types[1]));
        Assert.StartsWith($"'{otherGuid}',", Assert.Single(after, e => e.Id == types[2]).Args);
    }

    [Fact]
    public void DesiredTypeGuidCollidingWithAnotherDesiredRelation_BlocksBothPairs()
    {
        var writer = new StepWriter();
        var sources = new Dictionary<int, string>();
        foreach (string guid in new[] { NativeGuid, RelationGuid })
        {
            int type = writer.Add("IFCMEMBERTYPE", $"'{IfcGuid.FromName("old-type:" + guid)}',$,'Type',$,$,$,$,'500',$,.NOTDEFINED.");
            writer.Add("IFCRELDEFINESBYTYPE", $"'{IfcGuid.FromName("old-rel:" + guid)}',900,$,$,(#1001),#{type}");
            sources[type] = guid;
        }
        var before = Entities(writer);
        Assert.Equal((0, 0), Apply(writer, sources));
        Assert.Equal(before, Entities(writer));
    }

    [Theory]
    [InlineData("()")]
    [InlineData("$")]
    [InlineData("(#1001,$)")]
    [InlineData("(#-1001)")]
    [InlineData("(#99999999999999999999999)")]
    public void InvalidRelatedObjects_KeepsBothIdentities(string relatedObjects)
    {
        var writer = new StepWriter();
        int type = writer.Add("IFCMEMBERTYPE", $"'{IfcGuid.FromName("old-type")}',$,'Type',$,$,$,$,'500',$,.NOTDEFINED.");
        writer.Add("IFCRELDEFINESBYTYPE", $"'{IfcGuid.FromName("old-rel")}',900,$,$,{relatedObjects},#{type}");
        var before = Entities(writer);
        Assert.Equal((0, 0), Apply(writer, new Dictionary<int, string> { [type] = NativeGuid }));
        Assert.Equal(before, Entities(writer));
    }

    [Theory]
    [InlineData(0)] // no owning relationship
    [InlineData(1)] // two owners
    [InlineData(2)] // malformed owning relationship
    [InlineData(3)] // mapped entity isn't an IfcTypeObject
    [InlineData(4)] // invalid desired GUID
    [InlineData(5)] // missing source entity
    public void InvalidOrAmbiguousOwnership_KeepsAllBytes(int scenario)
    {
        var writer = new StepWriter();
        int type = writer.Add(scenario == 3 ? "IFCWALL" : "IFCMEMBERTYPE",
            $"'{IfcGuid.FromName("old-type")}',$,'Type',$,$,$,$,'500',$,.NOTDEFINED.");
        if (scenario != 0)
        {
            string args = scenario == 2 ? "'$',$,$,$" : $"'{IfcGuid.FromName("old-rel")}',900,$,$,(#1001),#{type}";
            writer.Add("IFCRELDEFINESBYTYPE", args);
            if (scenario == 1) writer.Add("IFCRELDEFINESBYTYPE", args);
        }
        var sources = new Dictionary<int, string> { [scenario == 5 ? 999 : type] = scenario == 4 ? "not a guid" : NativeGuid };
        var before = Entities(writer);
        Assert.Equal((0, 0), Apply(writer, sources));
        Assert.Equal(before, Entities(writer));
    }

    private static (StepWriter Writer, Dictionary<int, string> Sources) Mapped(string product, Action<Dictionary<int, Element>>? configure = null)
    {
        var elements = new Dictionary<int, Element>
        {
            [1] = new()
            {
                Id = 1, NativeMasterSymbolId = 500,
                Parameters = [new Parameter { Definition = new Definition { Name = "ELEM_TYPE_PARAM" }, Value = 500 }],
                Geometry = new GeometryData { ExactMappedInstances = [new GeoInstance { SymbolId = 601 }] },
            },
            [500] = new() { Id = 500, Name = "Display type", SerializedExportId = Guid.Parse("bef4655b-8c73-49b4-9bc6-1f0967d725fe") },
            [601] = new() { Id = 601, Name = "Geometry symbol", SerializedExportId = SymbolExportId },
        };
        configure?.Invoke(elements);
        var writer = new StepWriter();
        var method = typeof(IfcExporter).GetMethod("WriteTypeObjectsAndRelDefinesByType", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var sources = Assert.IsType<Dictionary<int, string>>(method.Invoke(null,
            [writer, 900, elements, new Dictionary<int, (int, int, string)> { [1] = (1001, 0, product) },
                new Dictionary<(string, string), int>(), new Dictionary<int, bool?>()]));
        return (writer, sources);
    }

    private static (int Types, int Relationships) Apply(StepWriter writer, Dictionary<int, string> sources)
    {
        var method = typeof(IfcExporter).GetMethod("ApplyTypeGlobalIds", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<(int, int)>(method.Invoke(null, [writer, sources]));
    }

    private static (int Id, string Type, string Args)[] Entities(StepWriter writer) =>
        Enumerable.Range(0, writer.EntityCount).Select(writer.EntityAt).ToArray();
}
