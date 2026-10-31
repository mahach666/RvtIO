using RvtIO.Ifc;
using RvtIO.Model;
using System.Text.RegularExpressions;
using Xunit;

namespace RvtIO.Tests;

public class IfcProxyCompositionTypeTests
{
    [Theory]
    [InlineData("arbitrary authored name", null, null)]
    [InlineData("ELEMENT", null, null)]
    [InlineData("COMPLEX", null, null)]
    [InlineData("PARTIAL", null, null)]
    [InlineData("Native product", "IfcExportType", "USERDEFINED")]
    [InlineData("Native product", "IfcType", "COMPLEX")]
    [InlineData("Native product", "IfcElementCompositionType", "PARTIAL")]
    [InlineData("Native product", "IfcExportAs", "IfcBuildingElementProxy")]
    public void Ifc2X3ProxyCompositionIsUnsetIndependentOfNamesAndPredefinedOverrides(
        string name, string? overrideName, string? overrideValue)
    {
        var info = new RevitFileInfo();
        var proxy = IfcExporterSlabCommonPropertySetTests.Slab(100, 200, name, -2000151, "OST_GenericModel", null);
        if (overrideName != null) proxy.Parameters.Add(new Parameter
        {
            Definition = new Definition { Name = overrideName }, StorageType = StorageType.String, Value = overrideValue,
        });
        // The current profile writes a storey only for a building-storey level, so the file has one
        info.Elements.Add(new Element
        {
            Id = 10, TypeId = 1010, CategoryId = -2000240, Category = "OST_Levels", Name = "L1", IsNativeLevel = true,
            SerializedFamilyId = -1, SerializedUnplacedOwnerId = -1, SerializedOwnerDbViewId = -1,
            Parameters =
            [
                new Parameter { Id = -1007102, StorageType = StorageType.Double, Value = 0.0 },
                new Parameter { Id = -1007111, StorageType = StorageType.Integer, Value = 1 },
            ],
        });
        info.Elements.Add(proxy);
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-proxy-composition-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter().Export(info, path);
            string text = File.ReadAllText(path);
            var product = Assert.Single(Regex.Matches(text, @"#\d+=IFCBUILDINGELEMENTPROXY\(([^\r\n]*)\);"));
            Assert.EndsWith(",'100',$", product.Groups[1].Value);
            Assert.Matches(@",#\d+,#\d+,'100',\$$", product.Groups[1].Value);
            Assert.Contains("'Pset_QuantityTakeOff'", text);
            // Spatial composition is a different IFC attribute and stays ELEMENT.
            Assert.Matches(@"#\d+=IFCBUILDING\([^\r\n]*,\.ELEMENT\.,", text);
            Assert.Matches(@"#\d+=IFCBUILDINGSTOREY\([^\r\n]*,\.ELEMENT\.,", text);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
