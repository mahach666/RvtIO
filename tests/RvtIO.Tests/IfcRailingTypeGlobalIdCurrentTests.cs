using System.Reflection;
using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>A railing has no geometry symbol, and in the current profile its IfcRailingType takes the ExportId of the type element
/// (the GlobalId of the type XOR the GlobalId of a railing equals the XOR of their element ids).</summary>
public class IfcRailingTypeGlobalIdCurrentTests
{
    private static readonly Guid TypeExportId = Guid.Parse("1f34752d-926d-4ab8-8425-c6d81fe02dab");

    [Fact]
    public void RailingType_TakesTheExportIdOfTheTypeElement()
    {
        var (text, sources) = Write(currentProfile: true, TypeExportId);
        Assert.Single(Regex.Matches(text, "=IFCRAILINGTYPE\\("));
        Assert.Equal(IfcGuid.ToIfcGuid(TypeExportId), Assert.Single(sources).Value);
    }

    [Fact]
    public void RailingType_WithoutAnExportId_KeepsTheSyntheticGlobalId()
    {
        var (text, sources) = Write(currentProfile: true, null);
        Assert.Single(Regex.Matches(text, "=IFCRAILINGTYPE\\("));
        Assert.Empty(sources);
    }

    [Fact]
    public void OlderRelease_KeepsTheSyntheticGlobalId()
    {
        var (_, sources) = Write(currentProfile: false, TypeExportId);
        Assert.Empty(sources);
    }

    private static (string Text, Dictionary<int, string> Sources) Write(bool currentProfile, Guid? typeExportId)
    {
        var elements = new Dictionary<int, Element>
        {
            [500] = new() { Id = 500, Name = "Railing type", CategoryId = -2000126, SerializedExportId = typeExportId },
            [1] = new()
            {
                Id = 1, CategoryId = -2000126, NativeMasterSymbolId = 500,
                Parameters = [new Parameter
                {
                    Id = -1002050, Definition = new Definition { Name = "ELEM_TYPE_PARAM" },
                    StorageType = StorageType.ElementId, Value = 500,
                }],
            },
        };
        var products = new Dictionary<int, (int entityId, int placementId, string ifcType)> { [1] = (1001, 0, "IFCRAILING") };
        var writer = new StepWriter();
        var method = typeof(IfcExporter).GetMethod("WriteTypeObjectsAndRelDefinesByTypeCore",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var sources = Assert.IsType<Dictionary<int, string>>(method.Invoke(null, [writer, 900, elements, products,
            new Dictionary<(string PropertyName, string ValueExpr), int>(), new Dictionary<int, bool?>(), currentProfile]));
        using var text = new StringWriter();
        writer.WriteTo(text, "test.ifc", "IFC2X3", "test", "test");
        return (text.ToString(), sources);
    }
}
