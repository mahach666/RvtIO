using System.Text.Json;
using RvtIO.Ifc;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class IfcCeilingTypeNameTests
{
    [Fact]
    public void NativeTypeName_UsesItsOwnSymbolInfoAndResourceFamily_WithoutChangingPublicData()
    {
        var (ceiling, type, state) = Scene();
        byte[] block = HostTypeLayerSetNameTests.Block(646);
        state.HostTypeCompoundStructures.Clear();
        RvtFileReader.RecordHostTypeCompoundStructure(state, block, 0,
            BitConverter.ToUInt32(block, 8), 646, 500);
        Assert.Equal("Own type", state.HostTypeCompoundStructures[500].TypeName);
        string before = JsonSerializer.Serialize(new[] { ceiling, type });
        Assert.Equal(1, ElementBuilder.ApplyHostTypeCompoundStructures([ceiling, type], state));
        Assert.Equal(before, JsonSerializer.Serialize(new[] { ceiling, type }));
        string ifc = Export(ceiling, type);
        Assert.Contains("'Native family:Own type'", TypeLine(ifc));
        Assert.DoesNotContain("Legacy", TypeLine(ifc));
        Assert.Equal(before, JsonSerializer.Serialize(new[] { ceiling, type }));
    }

    [Theory]
    [InlineData("missing_name")] [InlineData("blank_name")]
    [InlineData("missing_family")] [InlineData("missing_host")]
    [InlineData("duplicate_host")] [InlineData("duplicate_system")]
    [InlineData("duplicate_type")] [InlineData("block_only")]
    [InlineData("wrong_host_class")] [InlineData("wrong_root_class")]
    [InlineData("wrong_category")] [InlineData("missing_type_marker")]
    public void MissingOrConflictingOwnValidation_RetainsTheLegacyTypeName(string mutation)
    {
        var (ceiling, type, state) = Scene();
        var elements = new List<Element> { ceiling, type };
        switch (mutation)
        {
            case "missing_name": state.HostTypeCompoundStructures[500] = (1, 646, [], null); break;
            case "blank_name": state.HostTypeCompoundStructures[500] = (1, 646, [], " \t"); break;
            case "missing_family": type.ValidatedSystemFamilyName = null; break;
            case "missing_host": state.HostTypeCompoundStructures.Clear(); break;
            case "duplicate_host": state.HostTypeCompoundStructures[500] = (2, 646, null, null); break;
            case "duplicate_system": state.SystemTypeRoots[500] = (2, 646, null); break;
            case "duplicate_type": elements.Add(new Element { Id = 500 }); break;
            case "block_only": type.IsBlockOnly = true; break;
            case "wrong_host_class": state.HostTypeCompoundStructures[500] = (1, 512, [], "Wrong type"); break;
            case "wrong_root_class": state.SystemTypeRoots[500] = (1, 512, null); break;
            case "wrong_category": type.CategoryId = -2000032; break;
            case "missing_type_marker": type.SerializedSystemTypeClassId = null; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        ElementBuilder.ApplyHostTypeCompoundStructures(elements, state);
        Assert.Contains("'Legacy family:Legacy type'", TypeLine(Export(ceiling, type)));
    }

    [Fact]
    public void ExistingOwnFamilyTableName_KeepsItsPreviousPriority()
    {
        var (ceiling, type, state) = Scene();
        ElementBuilder.ApplyHostTypeCompoundStructures([ceiling, type], state);
        type.ValidatedIfcTypeObjectName = "Own table family:Own table type";
        Assert.Contains("'Own table family:Own table type'", TypeLine(Export(ceiling, type)));
    }

    [Fact]
    public void OnlyTypeNameChanges_ProductNameAndAllOtherEntityLinesStayUnchanged()
    {
        var (ceiling, type, state) = Scene();
        string before = Export(ceiling, type);
        ElementBuilder.ApplyHostTypeCompoundStructures([ceiling, type], state);
        string after = Export(ceiling, type);
        Assert.NotEqual(TypeLine(before), TypeLine(after));
        Assert.Equal(EntityLinesExceptCoveringType(before), EntityLinesExceptCoveringType(after));
        Assert.Equal(TypeLine(before).Replace("'Legacy family:Legacy type'", "'Native family:Own type'"), TypeLine(after));
        Assert.Equal(0, ElementBuilder.ApplyHostTypeCompoundStructures([ceiling, type], new ParsingState()));
        Assert.Equal(TypeLine(before), TypeLine(Export(ceiling, type)));
    }

    private static (Element, Element, ParsingState) Scene()
    {
        double[][] points = [[0, 0, 0], [4, 0, 0], [4, 3, 0], [0, 3, 0],
            [0, 0, 2], [4, 0, 2], [4, 3, 2], [0, 3, 2]];
        int[][] faces = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]];
        var ceiling = new Element
        {
            Id = 100, TypeId = 500, CategoryId = -2000038, Category = "OST_Ceilings",
            Name = "Occurrence name", IsDiscovered = true, References = [3],
            Parameters = [new Parameter { Id = -1002050, Value = 500, StorageType = StorageType.ElementId,
                Definition = new Definition { Id = -1002050, Name = "ELEM_TYPE_PARAM" } },
                new Parameter { Id = -1012100, Value = 3, StorageType = StorageType.ElementId }],
            BoundingBox = [0, 0, 0, 4, 3, 2],
            Geometry = new GeometryData { BoundingBox = [0, 0, 0, 4, 3, 2],
                Faces = faces.Select(indices => new GeoFace { OuterVertices = indices.Select(index => points[index]).ToList() }).ToList(),
                BrepSolids = [new GeoBrepSolid { IsClosed = true, FaceIndices = [0, 1, 2, 3, 4, 5] }] },
        };
        var type = new Element { Id = 500, TypeId = -1, CategoryId = -2000038,
            Name = "Legacy type", FamilyName = "Legacy family", ValidatedSystemFamilyName = "Native family" };
        var state = new ParsingState();
        state.SystemOccurrenceTypeIds[100] = (1, 642, 500);
        state.SystemTypeRoots[500] = (1, 646, null);
        state.HostTypeCompoundStructures[500] = (1, 646, [], "Own type");
        ElementBuilder.ApplySerializedSystemTypeIdentity([ceiling, type], state);
        return (ceiling, type, state);
    }

    private static string TypeLine(string ifc) => Assert.Single(ifc.Split('\n'), line => line.Contains("=IFCCOVERINGTYPE("));

    private static string[] EntityLinesExceptCoveringType(string ifc) => ifc.Split('\n')
        .Where(line => line.StartsWith('#') && !line.Contains("=IFCCOVERINGTYPE(")).ToArray();

    private static string Export(params Element[] elements)
    {
        var info = new RevitFileInfo(); info.Elements.AddRange(elements);
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-ceiling-type-name-{Guid.NewGuid():N}.ifc");
        try { new IfcExporter() { Profile = IfcProfile.Legacy }.Export(info, path); return File.ReadAllText(path); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
