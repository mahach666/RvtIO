using System.Reflection;
using RvtIO.Compact;
using RvtIO.Ifc;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>
/// The two atoms of the framing "Orientation" rule (both are 0 exactly when the beam has an associated sketch plane,
/// the condition of its "Work Plane"; see <see cref="CompactFamilyInstanceFacts"/>) and the rows of the category to IFC class table
/// added for Revit 2023 to 2026.
/// </summary>
public class BeamOrientationRuleTests
{
    private static int Tag(string name) => TfBlockDeserializer.Schemas.Single(pair => pair.Value.Name == name).Key;

    private static Element Beam(int? sketchPlane) => new()
    {
        Id = 1,
        NativeRecordClass = Tag("FamilyInstance"),
        NativeListFields = sketchPlane is int plane
            ? new Dictionary<string, int> { [TfClassNames.Key("FamilyInstance", "m_assocSketchPlaneId")] = plane }
            : new Dictionary<string, int>(),
    };

    [Theory]
    [InlineData("unmodelled:getMemberIndexToTagMap")]
    [InlineData("unmodelled:getMemberIndexToTagMap#2")]
    public void AnAssociatedSketchPlane_GivesZero_NoneGivesOne(string fact)
    {
        Assert.Equal(0, CompactFamilyInstanceFacts.Get(fact, Beam(119222), null));
        Assert.Equal(0, CompactFamilyInstanceFacts.Get(fact, Beam(0), null));
        Assert.Equal(1, CompactFamilyInstanceFacts.Get(fact, Beam(-1), null));
    }

    [Fact]
    public void NoDecodedSketchPlane_IsUnknownNotZero()
    {
        Assert.Null(CompactFamilyInstanceFacts.Get("unmodelled:getMemberIndexToTagMap", Beam(null), null));
    }

    [Fact]
    public void AnElementThatIsNotAFamilyInstance_HasNoFact()
    {
        var family = new Element { Id = 2, NativeRecordClass = Tag("Family") };
        Assert.Null(CompactFamilyInstanceFacts.Get("unmodelled:getMemberIndexToTagMap", family, null));
    }
}

public class IfcCategoryTableRowsTests
{
    private static string? Map(int? category)
    {
        var method = typeof(IfcExporter).GetMethod("MapCategoryToIfcType", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (string?)method.Invoke(null, [category]);
    }

    // Category to IFC class rows seen in Revit 2023 to 2026 samples
    [Theory]
    [InlineData(-2001060, "IFCBUILDINGELEMENTPROXY")]   // OST_ElectricalFixtures
    [InlineData(-2008128, "IFCFLOWFITTING")]            // OST_ConduitFitting
    [InlineData(-2008132, "IFCFLOWSEGMENT")]            // OST_Conduit
    [InlineData(-2008020, "IFCFLOWSEGMENT")]            // OST_FlexDuctCurves
    [InlineData(-2008087, "IFCBUILDINGELEMENTPROXY")]   // OST_LightingDevices
    [InlineData(-2008083, "IFCFLOWTERMINAL")]           // OST_DataDevices
    [InlineData(-2001140, "IFCBUILDINGELEMENTPROXY")]   // OST_MechanicalEquipment
    [InlineData(-2001180, "IFCBUILDINGELEMENTPROXY")]   // OST_Parking
    [InlineData(-2008234, "IFCBUILDINGELEMENTPROXY")]   // OST_PlumbingEquipment
    [InlineData(-2009030, "IFCBUILDINGELEMENTPROXY")]   // OST_StructConnections
    [InlineData(-2001392, "IFCBUILDINGELEMENTPROXY")]   // OST_EdgeSlab
    [InlineData(-2001354, "IFCPLATE")]                  // OST_StructuralStiffener
    [InlineData(-2001079, "IFCBUILDINGELEMENTPROXY")]   // OST_Toposolid
    public void Category_MapsToTheClassReferenceWrites(int category, string expected) => Assert.Equal(expected, Map(category));

    [Theory]
    [InlineData(-2000240)]   // OST_Levels
    [InlineData(-2000220)]   // OST_Grids
    [InlineData(-123456)]    // not a category of the table
    [InlineData(null)]
    public void SpatialOrUnknownCategory_HasNoProductClass(int? category) => Assert.Null(Map(category));
}
