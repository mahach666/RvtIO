using RvtIO.Ifc;
using Xunit;

namespace RvtIO.Tests;

public class TypePropertySetGlobalIdTests
{
    private const string NativeTypeGuid = "0VD7KjacrAk8GbnjWVu2sh";
    private static string Expected(string name) => IfcGuid.FromMd5($"IfcPropertySet:{name}:{NativeTypeGuid}");

    [Theory]
    [InlineData("IFCMEMBERTYPE", "Pset_MemberCommon")]
    [InlineData("IFCPLATETYPE", "Pset_PlateCommon")]
    [InlineData("IFCCOLUMNTYPE", "Pset_ColumnCommon")]
    [InlineData("IFCWALLTYPE", "Pset_WallCommon")]
    [InlineData("IFCCURTAINWALLTYPE", "Pset_CurtainWallCommon")]
    [InlineData("IFCRAILINGTYPE", "Pset_RailingCommon")]
    [InlineData("IFCDOORSTYLE", "Свойства 'O' \\ #9")]
    [InlineData("IFCWINDOWSTYLE", "")]
    public void ExactWrittenType_UsesItsGlobalIdAndDecodedPsetName(string typeClass, string name)
    {
        var (writer, type, pset) = Model(typeClass, name);
        var before = Entities(writer);
        Assert.Equal(1, Apply(writer, type));
        var after = Entities(writer);
        Assert.Equal(before.Length, after.Length);
        for (int i = 0; i < before.Length; i++)
        {
            Assert.Equal(before[i].Id, after[i].Id);
            Assert.Equal(before[i].Type, after[i].Type);
            Assert.Equal(before[i].Id == pset ? $"'{Expected(name)}'{before[i].Args[24..]}" : before[i].Args,
                after[i].Args);
        }
        Assert.Equal(0, Apply(writer, type));
        Assert.Equal(after, Entities(writer));
    }

    [Theory]
    [InlineData("no-validation")]
    [InlineData("unapplied-validation")]
    [InlineData("invalid-guid")]
    [InlineData("wrong-class")]
    [InlineData("duplicate-type-guid")]
    [InlineData("duplicate-old-pset-guid")]
    [InlineData("occupied-target")]
    [InlineData("occurrence-shared")]
    [InlineData("type-shared")]
    [InlineData("twice-listed")]
    [InlineData("duplicate-name")]
    [InlineData("invalid-name")]
    [InlineData("unknown-definition")]
    [InlineData("duplicate-pset-id")]
    [InlineData("duplicate-type-id")]
    [InlineData("duplicate-pset-id-nonpset")]
    public void AmbiguousOrUnvalidatedOwnership_KeepsAllArguments(string mode)
    {
        var (writer, type, pset) = Model(mode == "wrong-class" ? "IFCWALL" : "IFCMEMBERTYPE", "Pset_MemberCommon");
        var validation = new Dictionary<int, string> { [type] = NativeTypeGuid };
        int typeIndex = Index(writer, type);
        int psetIndex = Index(writer, pset);
        if (mode == "no-validation") validation.Clear();
        if (mode == "unapplied-validation")
            writer.ReplaceArgs(typeIndex, $"'{IfcGuid.FromName("rejected-type")}'{writer.EntityAt(typeIndex).Args[24..]}");
        if (mode == "invalid-guid") validation[type] = "xxxxxxxxxxxxxxxxxxxxxx";
        if (mode == "duplicate-type-guid")
            writer.Add("IFCWALL", $"'{NativeTypeGuid}',$,$,$,$,$,$,$");
        if (mode == "duplicate-old-pset-guid")
            writer.Add("IFCPROPERTYSET", writer.EntityAt(psetIndex).Args);
        if (mode == "occupied-target")
            writer.Add("IFCWALL", $"'{Expected("Pset_MemberCommon")}',#1,$,$,$,$,$,$");
        if (mode == "occurrence-shared")
            writer.Add("IFCRELDEFINESBYPROPERTIES", $"'{IfcGuid.FromName("shared")}',#1,$,$,(#99),#{pset}");
        if (mode == "type-shared")
            writer.Add("IFCMEMBERTYPE", $"'{IfcGuid.FromName("other-type")}',#1,'T',$,$,(#{pset}),$,'9',$,.NOTDEFINED.");
        if (mode == "twice-listed")
            writer.ReplaceArgs(typeIndex, writer.EntityAt(typeIndex).Args.Replace($"(#{pset})", $"(#{pset},#{pset})"));
        if (mode == "duplicate-name")
        {
            int second = writer.Add("IFCPROPERTYSET", $"'{IfcGuid.FromName("another-set")}',#1,'Pset_MemberCommon',$,(#2)");
            writer.ReplaceArgs(typeIndex, writer.EntityAt(typeIndex).Args.Replace($"(#{pset})", $"(#{pset},#{second})"));
        }
        if (mode == "invalid-name")
            writer.ReplaceArgs(psetIndex, writer.EntityAt(psetIndex).Args.Replace("'Pset_MemberCommon'", "$"));
        if (mode == "unknown-definition")
            writer.ReplaceArgs(typeIndex, writer.EntityAt(typeIndex).Args.Replace($"(#{pset})", "(#999999)"));
        if (mode == "duplicate-pset-id")
            writer.AddWithId(pset, "IFCPROPERTYSET", $"'{IfcGuid.FromName("duplicate-id")}',#1,'Other',$,(#2)");
        if (mode == "duplicate-type-id")
            writer.AddWithId(type, "IFCMEMBERTYPE", $"'{IfcGuid.FromName("duplicate-type-id")}',#1,'T',$,$,$,$,'9',$,.NOTDEFINED.");
        if (mode == "duplicate-pset-id-nonpset")
            writer.AddWithId(pset, "IFCGROUP", $"'{IfcGuid.FromName("duplicate-nonpset-id")}',#1,$,$,$");
        var before = Entities(writer);
        Assert.Equal(0, IfcExporter.ApplyTypePropertySetGlobalIds(writer, validation));
        Assert.Equal(before, Entities(writer));
    }

    [Theory]
    [InlineData("$")]
    [InlineData("()")]
    [InlineData("(#3,$)")]
    [InlineData("(#-3)")]
    [InlineData("(#999999999999999999999)")]
    public void InvalidHasPropertySets_DoesNotProduceAnIdentity(string list)
    {
        var (writer, type, pset) = Model("IFCMEMBERTYPE", "Pset_MemberCommon");
        int index = Index(writer, type);
        writer.ReplaceArgs(index, writer.EntityAt(index).Args.Replace($"(#{pset})", list));
        var before = Entities(writer);
        Assert.Equal(0, Apply(writer, type));
        Assert.Equal(before, Entities(writer));
    }

    [Fact]
    public void QuotedHashReference_IsNotAPsetOwner()
    {
        var (writer, type, pset) = Model("IFCMEMBERTYPE", "Pset_MemberCommon");
        writer.Add("IFCWALL", $"'{IfcGuid.FromName("wall")}',#1,'contains #{pset} and ''quotes''',$,$,$,$,$");
        Assert.Equal(1, Apply(writer, type));
    }

    [Fact]
    public void AnyUnquotedPsetReferenceOutsideType_BlocksTheRewrite()
    {
        var (writer, type, pset) = Model("IFCMEMBERTYPE", "Pset_MemberCommon");
        writer.Add("IFCGROUP", $"'{IfcGuid.FromName("malformed-ref")}',#1,$,$,#{pset}");
        Assert.Equal(0, Apply(writer, type));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateDesiredTypeValidation_NeverChoosesAFirstWinner(bool reverse)
    {
        var (writer, type, _) = Model("IFCMEMBERTYPE", "Pset_MemberCommon");
        int other = writer.Add("IFCMEMBERTYPE", $"'{IfcGuid.FromName("other-type")}',#1,'T',$,$,$,$,'9',$,.NOTDEFINED.");
        var validation = reverse ? new Dictionary<int, string> { [other] = NativeTypeGuid, [type] = NativeTypeGuid }
            : new Dictionary<int, string> { [type] = NativeTypeGuid, [other] = NativeTypeGuid };
        Assert.Equal(0, IfcExporter.ApplyTypePropertySetGlobalIds(writer, validation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuidFreedByAnotherPsetRewrite_IsStillOccupiedInTheInitialSnapshot(bool reverse)
    {
        var (writer, type, pset) = Model("IFCMEMBERTYPE", "Pset_MemberCommon");
        string desired = Expected("Pset_MemberCommon");
        int other = writer.Add("IFCPROPERTYSET", $"'{desired}',#1,'Other',$,(#2)");
        int index = Index(writer, type);
        writer.ReplaceArgs(index, writer.EntityAt(index).Args.Replace($"(#{pset})",
            reverse ? $"(#{other},#{pset})" : $"(#{pset},#{other})"));
        string before = writer.EntityAt(Index(writer, pset)).Args;
        Assert.Equal(1, Apply(writer, type));
        Assert.Equal(before, writer.EntityAt(Index(writer, pset)).Args);
        Assert.StartsWith($"'{Expected("Other")}',", writer.EntityAt(Index(writer, other)).Args);
        // A second invocation sees a different authoritative snapshot; the formerly
        // occupied GUID is now available. No winner depends on first-pass traversal.
        Assert.Equal(1, Apply(writer, type));
        Assert.Equal(0, Apply(writer, type));
    }

    private static int Apply(StepWriter writer, int type) =>
        IfcExporter.ApplyTypePropertySetGlobalIds(writer, new Dictionary<int, string> { [type] = NativeTypeGuid });

    private static (StepWriter Writer, int Type, int Pset) Model(string typeClass, string name)
    {
        var writer = new StepWriter();
        writer.Add("IFCOWNERHISTORY", "$,$,$,.NOCHANGE.,$,$,$,0");
        writer.Add("IFCPROPERTYSINGLEVALUE", "'IsExternal',$,IFCBOOLEAN(.F.),$");
        int pset = writer.Add("IFCPROPERTYSET", $"'{IfcGuid.FromName("old-pset")}',#1,{StepWriter.Str(name)},$,(#2)");
        int type = writer.Add(typeClass, $"'{NativeTypeGuid}',#1,'Type',$,$,(#{pset}),$,'500',$,.NOTDEFINED.");
        return (writer, type, pset);
    }

    private static int Index(StepWriter writer, int id) =>
        Enumerable.Range(0, writer.EntityCount).Single(i => writer.EntityAt(i).Id == id);

    private static (int Id, string Type, string Args)[] Entities(StepWriter writer) =>
        Enumerable.Range(0, writer.EntityCount).Select(writer.EntityAt).ToArray();
}
