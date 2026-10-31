using System.Reflection;
using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

public class IfcTypeClassPartitionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedCanonicalType_IsPartitionedByIfcTypeClass(bool reverse)
    {
        string text = Write(reverse, "IFCWALLSTANDARDCASE", "IFCCURTAINWALL");
        int wall = Id(text, "IFCWALLTYPE");
        int curtain = Id(text, "IFCCURTAINWALLTYPE");
        Assert.NotEqual(wall, curtain);
        Assert.Contains($"(#1001),#{wall});", text);
        Assert.Contains($"(#1002),#{curtain});", text);
        Assert.Equal(2, Regex.Matches(text, "=IFCRELDEFINESBYTYPE\\(").Count);
        foreach (string cls in new[] { "IFCWALLTYPE", "IFCCURTAINWALLTYPE" })
        {
            Assert.Contains($"'{IfcGuid.FromName($"type:500:{cls}")}'", text);
            Assert.Contains($"'{IfcGuid.FromName($"rel-defines-by-type:500:{cls}")}'", text);
        }
    }

    [Theory]
    [InlineData("IFCWALL", "IFCWALLSTANDARDCASE")]
    [InlineData("IFCWALLSTANDARDCASE", "IFCWALLSTANDARDCASE")]
    public void CompatibleProductClasses_KeepOneLegacyTypeAndGuids(string first, string second)
    {
        string text = Write(false, first, second);
        int type = Id(text, "IFCWALLTYPE");
        Assert.Contains($"(#1001,#1002),#{type});", text);
        Assert.Contains($"'{IfcGuid.FromName("type:500")}'", text);
        Assert.Contains($"'{IfcGuid.FromName("rel-defines-by-type:500")}'", text);
        Assert.Single(Regex.Matches(text, "=IFCRELDEFINESBYTYPE\\("));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DifferentTypeShapes_DoNotLeakPropertySets(bool reverse)
    {
        string text = Write(reverse, "IFCDOOR", "IFCRAILING");
        int door = Id(text, "IFCDOORSTYLE");
        int railing = Id(text, "IFCRAILINGTYPE");
        Assert.Contains($"(#1001),#{door});", text);
        Assert.Contains($"(#1002),#{railing});", text);
        string doorLine = Line(text, door);
        Assert.Contains("'Shared type',$,$,$,$,'500',", doorLine);
        Assert.EndsWith(",.NOTDEFINED.,.NOTDEFINED.,.F.,.F.);", doorLine);
        Assert.Contains("'Pset_RailingCommon'", text);
        Assert.Contains("IFCBOOLEAN(.F.)", text);
        Assert.Matches(@",\(#\d+\),\$,'500',\$,\.NOTDEFINED\.\);$", Line(text, railing));
    }

    [Fact]
    public void UnexportedOrUnresolvedProducts_DoNotSplitLegacyGroup()
    {
        string text = Write(false, "IFCWALLSTANDARDCASE", "IFCCURTAINWALL", includeSecond: false);
        Assert.DoesNotContain("=IFCCURTAINWALLTYPE(", text);
        Assert.Contains($"'{IfcGuid.FromName("type:500")}'", text);
        text = Write(false, "IFCWALLSTANDARDCASE", "IFCCURTAINWALL", secondType: 999);
        Assert.DoesNotContain("=IFCCURTAINWALLTYPE(", text);
        Assert.Contains($"'{IfcGuid.FromName("type:500")}'", text);
    }

    [Theory]
    [InlineData("IFCFLOWTERMINAL", -2001120, "IFCLIGHTFIXTURETYPE", 10, ".NOTDEFINED.")]
    [InlineData("IFCFURNISHINGELEMENT", -2001100, "IFCSYSTEMFURNITUREELEMENTTYPE", 9, "$")]
    [InlineData("IFCFLOWSEGMENT", -2008000, "IFCDUCTSEGMENTTYPE", 10, ".NOTDEFINED.")]
    public void CanonicalTypeCategory_SelectsMeasuredSubtypeAndSchemaShape(
        string productClass, int category, string typeClass, int argumentCount, string lastArgument)
    {
        string text = Write(false, productClass, productClass, typeCategory: category);
        int type = Id(text, typeClass);
        string line = Line(text, type);
        string[] args = line[(line.IndexOf('(') + 1)..^2].Split(',');
        Assert.Equal(argumentCount, args.Length);
        Assert.Equal(lastArgument, args[^1]);
        Assert.Equal("'500'", args[7]);
        Assert.Equal($"'{IfcGuid.FromName("type:500")}'", args[0]);
        Assert.Contains($"(#1001,#1002),#{type});", text);
        Assert.Contains($"'{IfcGuid.FromName("rel-defines-by-type:500")}'", text);
        Assert.DoesNotContain("=IFCPROPERTYSET(", text);
    }

    [Theory]
    [InlineData("IFCFLOWTERMINAL", null, -2001120, "IFCDISTRIBUTIONELEMENTTYPE")]
    [InlineData("IFCFLOWTERMINAL", -2001160, -2001120, "IFCDISTRIBUTIONELEMENTTYPE")]
    [InlineData("IFCFURNISHINGELEMENT", null, -2001100, "IFCFURNITURETYPE")]
    [InlineData("IFCFURNISHINGELEMENT", -2000080, -2001100, "IFCFURNITURETYPE")]
    [InlineData("IFCFLOWSEGMENT", null, -2008000, "IFCDISTRIBUTIONELEMENTTYPE")]
    [InlineData("IFCFLOWSEGMENT", -2008016, -2008000, "IFCDISTRIBUTIONELEMENTTYPE")]
    [InlineData("IFCWALL", -2001120, -2001120, "IFCWALLTYPE")]
    [InlineData("IFCFLOWTERMINAL", -2008000, -2008000, "IFCDISTRIBUTIONELEMENTTYPE")]
    public void OtherOrMissingTypeCategory_DoesNotBorrowOccurrenceCategory(
        string productClass, int? typeCategory, int occurrenceCategory, string expectedTypeClass)
    {
        string text = Write(false, productClass, productClass,
            typeCategory: typeCategory, occurrenceCategory: occurrenceCategory);
        Assert.Contains($"(#1001,#1002),#{Id(text, expectedTypeClass)});", text);
    }

    [Theory]
    [InlineData("IFCWALL", "IFCWALLTYPE")]
    [InlineData("IFCSLAB", "IFCSLABTYPE")]
    [InlineData("IFCMEMBER", "IFCMEMBERTYPE")]
    [InlineData("IFCPLATE", "IFCPLATETYPE")]
    [InlineData("IFCCOLUMN", "IFCCOLUMNTYPE")]
    [InlineData("IFCBEAM", "IFCBEAMTYPE")]
    [InlineData("IFCCOVERING", "IFCCOVERINGTYPE")]
    [InlineData("IFCRAILING", "IFCRAILINGTYPE")]
    [InlineData("IFCCURTAINWALL", "IFCCURTAINWALLTYPE")]
    [InlineData("IFCBUILDINGELEMENTPROXY", "IFCBUILDINGELEMENTPROXYTYPE")]
    [InlineData("IFCSTAIRFLIGHT", "IFCSTAIRFLIGHTTYPE")]
    public void RequiredPredefinedType_UsesExplicitUnknownInsteadOfMissingAttribute(
        string productClass, string typeClass)
    {
        string text = Write(false, productClass, productClass);
        int type = Id(text, typeClass);
        string line = Line(text, type);
        string[] args = line[(line.IndexOf('(') + 1)..^2].Split(',');
        Assert.Equal(10, args.Length);
        Assert.Equal(".NOTDEFINED.", args[9]);
        Assert.Equal("$", args[8]); // ElementType is optional, not the enum slot.
        // A curtain panel type is tagged by its first panel; every other type by the type element.
        Assert.Equal(productClass == "IFCPLATE" ? "'1'" : "'500'", args[7]);
        Assert.Equal("'Shared type'", args[2]);
        Assert.Equal($"'{IfcGuid.FromName("type:500")}'", args[0]);
        Assert.Contains($"(#1001,#1002),#{type});", text);
        Assert.Single(Regex.Matches(text, "=IFCRELDEFINESBYTYPE\\("));
        if (productClass == "IFCRAILING")
            Assert.Matches(@"^\(#\d+\)$", args[5]);
        else
            Assert.Equal("$", args[5]);
    }

    [Theory]
    [InlineData("IFCFURNISHINGELEMENT", "IFCFURNITURETYPE", 10)]
    [InlineData("IFCFLOWTERMINAL", "IFCDISTRIBUTIONELEMENTTYPE", 9)]
    [InlineData("IFCFLOWSEGMENT", "IFCDISTRIBUTIONELEMENTTYPE", 9)]
    public void TypesWithoutPredefinedType_KeepLegacyShape(
        string productClass, string typeClass, int count)
    {
        string text = Write(false, productClass, productClass);
        string line = Line(text, Id(text, typeClass));
        string[] args = line[(line.IndexOf('(') + 1)..^2].Split(',');
        Assert.Equal(count, args.Length);
        Assert.Equal("$", args[^1]);
        Assert.DoesNotContain(".NOTDEFINED.", line);
    }

    [Theory]
    [InlineData("IFCWALLSTANDARDCASE", "IFCWALLTYPE", -2000011, "STANDARD")]
    [InlineData("IFCSLAB", "IFCSLABTYPE", -2000032, "FLOOR")]
    [InlineData("IFCSLAB", "IFCSLABTYPE", -2000920, "LANDING")]
    [InlineData("IFCCOVERING", "IFCCOVERINGTYPE", -2000038, "CEILING")]
    [InlineData("IFCMEMBER", "IFCMEMBERTYPE", -2000171, "MULLION")]
    [InlineData("IFCPLATE", "IFCPLATETYPE", -2000170, "CURTAIN_PANEL")]
    public void NativeTypeSource_SelectsConcretePredefinedType(string product, string type, int category, string value)
    {
        string text = Write(false, product, product, typeCategory: category, occurrenceCategory: category,
            configureType: e => e.SerializedBasicWallTypeFunction = 0);
        Assert.EndsWith($",.{value}.);", Line(text, Id(text, type)));
        Assert.Contains($"'{IfcGuid.FromName("type:500")}'", text);
    }

    [Theory]
    [InlineData("IFCWALL", "IFCWALLTYPE", -2000011)]
    [InlineData("IFCSLAB", "IFCSLABTYPE", -2001300)]
    [InlineData("IFCMEMBER", "IFCMEMBERTYPE", -2000123)]
    [InlineData("IFCBEAM", "IFCBEAMTYPE", -2001320)]
    [InlineData("IFCSTAIRFLIGHT", "IFCSTAIRFLIGHTTYPE", -2000919)]
    public void CategoryAlone_DoesNotInventOtherConcreteTypes(string product, string type, int category)
    {
        string text = Write(false, product, product, typeCategory: category, occurrenceCategory: category);
        Assert.EndsWith(",.NOTDEFINED.);", Line(text, Id(text, type)));
    }

    [Theory]
    [InlineData("IfcExportAs")] [InlineData("IfcExportType")] [InlineData("IfcType")]
    [InlineData("IfcExportAs[Type]")] [InlineData("IfcExportType[Type]")] [InlineData("IfcType[Type]")]
    [InlineData(" I F C Export Type ")]
    public void AuthoredOrAmbiguousOverride_BlocksCategoryDefault(string name)
    {
        string text = Write(false, "IFCSLAB", "IFCSLAB", typeCategory: -2000032, occurrenceCategory: -2000032,
            configureType: e => e.Parameters.Add(new Parameter
            {
                Definition = new Definition { Name = name }, StorageType = StorageType.String, Value = "ROOF",
            }));
        Assert.EndsWith(",.NOTDEFINED.);", Line(text, Id(text, "IFCSLABTYPE")));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void MixedOccurrenceSource_FailsClosedInEitherOrder(bool reverse, bool overrideValue)
    {
        string text = Write(reverse, "IFCSLAB", "IFCSLAB", typeCategory: -2000032, occurrenceCategory: -2000032,
            configureSecond: e =>
            {
                if (overrideValue) e.Parameters.Add(new Parameter { Id = -1019016, StorageType = StorageType.String, Value = "ROOF" });
                else e.CategoryId = -2000920;
            });
        Assert.EndsWith(",.NOTDEFINED.);", Line(text, Id(text, "IFCSLABTYPE")));
        Assert.Single(Regex.Matches(text, "=IFCSLABTYPE\\("));
    }

    private static string Write(bool reverse, string first, string second,
        bool includeSecond = true, int secondType = 500, int? typeCategory = null,
        int? occurrenceCategory = null, Action<Element>? configureType = null,
        Action<Element>? configureSecond = null)
    {
        Element Instance(int id, int type) => new()
        {
            Id = id,
            CategoryId = occurrenceCategory,
            NativeMasterSymbolId = type,
            Parameters = [new Parameter
            {
                Id = -1002050, Definition = new Definition { Name = "ELEM_TYPE_PARAM" },
                StorageType = StorageType.ElementId, Value = type,
            }],
        };
        var elements = new Dictionary<int, Element>
        {
            [500] = new() { Id = 500, Name = "Shared type", CategoryId = typeCategory },
            [1] = Instance(1, 500), [2] = Instance(2, secondType),
        };
        var products = new Dictionary<int, (int entityId, int placementId, string ifcType)>();
        configureType?.Invoke(elements[500]);
        configureSecond?.Invoke(elements[2]);
        foreach (int id in reverse ? new[] { 2, 1 } : new[] { 1, 2 })
            if (id == 1 || includeSecond)
                products.Add(id, (1000 + id, 0, id == 1 ? first : second));
        var writer = new StepWriter();
        var method = typeof(IfcExporter).GetMethod("WriteTypeObjectsAndRelDefinesByType",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        method.Invoke(null, [writer, 900, elements, products,
            new Dictionary<(string PropertyName, string ValueExpr), int>(), new Dictionary<int, bool?>()]);
        using var text = new StringWriter();
        writer.WriteTo(text, "test.ifc", "IFC2X3", "test", "test");
        return text.ToString();
    }

    private static int Id(string text, string cls) => int.Parse(
        Assert.Single(Regex.Matches(text, $@"#(\d+)={cls}\(")).Groups[1].Value);

    private static string Line(string text, int id) =>
        Assert.Single(text.Split('\n'), line => line.StartsWith($"#{id}=")).TrimEnd('\r');
}
