using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The current profile material associations of MEP products and of family products read from solids:
/// flow segments carry none, pipe insulation one best material, family products one entry per solid for the
/// first instance of a type and the distinct set for the later ones.</summary>
public class IfcMepMaterialsTests
{
    private const int PipeCurves = -2008044;      // OST_PipeCurves
    private const int PipeInsulations = -2008122; // OST_PipeInsulations
    private const int GenericModel = -2000151;    // OST_GenericModel

    [Theory]
    [InlineData(IfcProfile.Current, false)]
    [InlineData(IfcProfile.Legacy, true)]
    public void FlowSegment_HasNoMaterialInCurrent(IfcProfile version, bool expectsMaterial)
    {
        var pipe = Product(100, PipeCurves, "OST_PipeCurves", null, Prism(_ => 700));
        string ifc = Export(version, pipe, Material(700, "Copper"));
        Assert.Contains("=IFCFLOWSEGMENT(", ifc);
        Assert.Equal(expectsMaterial, ifc.Contains("IFCRELASSOCIATESMATERIAL("));
    }

    [Fact]
    public void PipeInsulation_TakesTheMaterialOfTheGreatestFaceAreaAlone()
    {
        // The two 4 x 3 end faces (area 24) carry "Cover"; the four sides (8 + 6 + 8 + 6 = 28) carry "Side" and win.
        var insulation = Product(100, PipeInsulations, "OST_PipeInsulations", null,
            Prism(face => face < 2 ? 710 : 700));
        string ifc = Export(IfcProfile.Current, insulation, Material(700, "Side"), Material(710, "Cover"));
        Assert.Equal("Side", MaterialOf(ifc, "IFCCOVERING", "100"));
    }

    [Fact]
    public void PipeInsulation_WithoutAFaceMaterialGetsNoNullMaterial()
    {
        var insulation = Product(100, PipeInsulations, "OST_PipeInsulations", null, Prism(_ => null));
        string ifc = Export(IfcProfile.Current, insulation, Material(700, "Unused"));
        Assert.Contains("=IFCCOVERING(", ifc);
        Assert.Empty(Associations(ifc));
        Assert.DoesNotContain("<Unnamed>", ifc);
    }

    [Fact]
    public void PipeInsulation_TakesItsTypeMaterialParameterWhenTheFacesHaveNone()
    {
        var type = Type(500, 710);
        var insulation = Product(100, PipeInsulations, "OST_PipeInsulations", 500, Prism(_ => null));
        string ifc = Export(IfcProfile.Current, type, insulation, Material(710, "Foam"));
        Assert.Equal("Foam", MaterialOf(ifc, "IFCCOVERING", "100"));
    }

    [Theory]
    [InlineData(IfcProfile.Current, true)]
    [InlineData(IfcProfile.Legacy, false)]
    public void MaterialKnownOnlyByItsOwnRecordName_IsReadFromTheFacesInCurrent(IfcProfile version, bool expected)
    {
        var symbol = Type(500, null, Solids(700));
        var product = Product(100, GenericModel, "OST_GenericModel", 500, Prism(_ => null));
        var material = Material(700, "Display name", shading: false);
        material.SerializedMaterialName = "Opening task";
        string ifc = Export(version, symbol, product, material);
        Assert.Equal(expected, ifc.Contains("IFCMATERIAL('Opening task')"));
    }

    [Fact]
    public void FaceStyleThatIsNotAMaterialElement_IsIgnored()
    {
        var symbol = Type(500, null, Solids(700));
        var product = Product(100, GenericModel, "OST_GenericModel", 500, Prism(_ => null));
        var notAMaterial = new Element { Id = 700, TypeId = -1, IsBlockOnly = true, CategoryId = -2000151, Name = "Not a material" };
        string ifc = Export(IfcProfile.Current, symbol, product, notAMaterial);
        Assert.DoesNotContain("Not a material", ifc);
        Assert.Equal("<Unnamed>", MaterialOf(ifc, "IFCBUILDINGELEMENTPROXY", "100").Trim());
    }

    [Fact]
    public void FirstInstanceOfAType_KeepsOneEntryPerSolid_LaterInstancesTheDistinctSetInIdOrder()
    {
        // Solids: Zinc (id 700), Brass (id 710), Zinc. The first instance lists them by material name, the second
        // takes the distinct set by element id.
        var symbol = Type(500, null, Solids(700, 710, 700));
        var first = Product(100, GenericModel, "OST_GenericModel", 500, Prism(_ => null));
        var second = Product(101, GenericModel, "OST_GenericModel", 500, Prism(_ => null));
        string ifc = Export(IfcProfile.Current, symbol, first, second, Material(700, "Zinc"), Material(710, "Brass"));
        Assert.Equal("[Brass|Zinc|Zinc]", MaterialOf(ifc, "IFCBUILDINGELEMENTPROXY", "100"));
        Assert.Equal("[Zinc|Brass]", MaterialOf(ifc, "IFCBUILDINGELEMENTPROXY", "101"));
    }

    [Fact]
    public void OneMaterialUnderEverySolid_KeepsOneEntryPerGeometryNode()
    {
        // Four Geometry nodes of one material: the first instance of the type lists all four, the later one takes the distinct set.
        var symbol = Type(500, null, InNodes(Solids(700, 700, 700, 700), solid => solid + 1));
        var first = Product(100, GenericModel, "OST_GenericModel", 500, Prism(_ => null));
        var second = Product(101, GenericModel, "OST_GenericModel", 500, Prism(_ => null));
        string ifc = Export(IfcProfile.Current, symbol, first, second, Material(700, "Task"));
        Assert.Equal("[Task|Task|Task|Task]", MaterialOf(ifc, "IFCBUILDINGELEMENTPROXY", "100"));
        Assert.Equal("Task", MaterialOf(ifc, "IFCBUILDINGELEMENTPROXY", "101"));
    }

    [Fact]
    public void DisjointShellsOfOneGeometryNode_AreOneSolid_WhoseMaterialIsTheDominantFaceMaterial()
    {
        // A 4-face "warning" shell sits in the same Geometry node as the pipe body; one solid is exported and takes the material of the
        // greatest face area, so the small shell adds no entry.
        var geometry = InNodes(Solids(700, 710, 700, 700), _ => 1);
        var symbol = Type(500, null, geometry);
        var product = Product(100, GenericModel, "OST_GenericModel", 500, Prism(_ => null));
        string ifc = Export(IfcProfile.Current, symbol, product, Material(700, "Steel"), Material(710, "Warning"));
        Assert.Equal("Steel", MaterialOf(ifc, "IFCBUILDINGELEMENTPROXY", "100"));
        Assert.DoesNotContain("IFCMATERIALLIST(", ifc);
    }

    [Fact]
    public void GeometryNodeReferenceDoesNotTake_IsLeftOut()
    {
        // A node with the visibility bits set (a negative id) is not exported.
        var symbol = Type(500, null, InNodes(Solids(700, 710), solid => solid == 0 ? 1 : -2));
        var product = Product(100, GenericModel, "OST_GenericModel", 500, Prism(_ => null));
        string ifc = Export(IfcProfile.Current, symbol, product, Material(700, "Zinc"), Material(710, "Hidden"));
        Assert.Equal("Zinc", MaterialOf(ifc, "IFCBUILDINGELEMENTPROXY", "100"));
        Assert.DoesNotContain("Hidden", ifc);
    }

    [Fact]
    public void GhostNode_IsAPieceOfTheProductWithTheMaterialOfItsFaces()
    {
        // A node exported as a solid without a polygon of ours (a node of display surfaces, or one identical to its neighbour) still counts:
        // the first instance lists it among the solids by material name, the later ones in the distinct set by element id.
        var geometry = InNodes(Solids(700, 710), solid => solid + 1);
        geometry.GhostNodes = [(3, -1, 720)];
        var symbol = Type(500, null, geometry);
        var first = Product(100, GenericModel, "OST_GenericModel", 500, Prism(_ => null));
        var second = Product(101, GenericModel, "OST_GenericModel", 500, Prism(_ => null));
        string ifc = Export(IfcProfile.Current, symbol, first, second,
            Material(700, "Aluminum"), Material(710, "Wood"), Material(720, "Brass"));
        Assert.Equal("[Aluminum|Brass|Wood]", MaterialOf(ifc, "IFCBUILDINGELEMENTPROXY", "100"));
        Assert.Equal("[Aluminum|Wood|Brass]", MaterialOf(ifc, "IFCBUILDINGELEMENTPROXY", "101"));
    }

    [Fact]
    public void GhostNode_WithoutAMaterialIsTheNullMaterial_AndOneWithAStyleTakesTheStyleMaterial()
    {
        var geometry = InNodes(Solids(700), _ => 1);
        geometry.GhostNodes = [(2, -1, 0), (3, 800, 0)];
        var symbol = Type(500, null, geometry);
        var product = Product(100, GenericModel, "OST_GenericModel", 500, Prism(_ => null));
        string ifc = Export(IfcProfile.Current, new Dictionary<int, (int, int)> { [800] = (710, 0) },
            symbol, product, Material(700, "Aluminum"), Material(710, "Style material"));
        // by material name: " <Unnamed>" is the null material of the shared name with its leading space
        Assert.Equal("[ <Unnamed>|Aluminum|Style material]", MaterialOf(ifc, "IFCBUILDINGELEMENTPROXY", "100"));
    }

    [Fact]
    public void NodeWithoutFaceMaterial_TakesTheMaterialOfItsGraphicsStyle()
    {
        // The best material starts from the material of the solid's graphics style.
        var geometry = InNodes(Solids((int?)null), _ => 1);
        foreach (var face in geometry.Faces) face.GeometryStyle = 800;
        var symbol = Type(500, null, geometry);
        var product = Product(100, GenericModel, "OST_GenericModel", 500, Prism(_ => null));
        string ifc = Export(IfcProfile.Current, new Dictionary<int, (int, int)> { [800] = (700, 0) },
            symbol, product, Material(700, "Style material"));
        Assert.Equal("Style material", MaterialOf(ifc, "IFCBUILDINGELEMENTPROXY", "100"));
    }

    [Fact]
    public void FirstInstanceList_IsOrderedByTheCategoryNameOfTheGraphicsStyle_ElseByTheMaterialName()
    {
        // Constituents are ordered by the name of the category of the solid's graphics style: "Zeta" is a subcategory of the file, -2000018 the built-in
        // window category named "Frame/Mullion"; the third solid has no style and is named after its material. A list ordered by material name alone
        // would be Aluminum, Brass, Wood.
        var geometry = InNodes(Solids(700, 710, 720), solid => solid + 1);
        WithStyles(geometry, 801, 802, 0);
        var symbol = Type(500, null, geometry);
        var first = Product(100, GenericModel, "OST_GenericModel", 500, Prism(_ => null));
        string ifc = Export(IfcProfile.Current,
            new Dictionary<int, (int, int)> { [801] = (0, 901), [802] = (0, -2000018) },
            new Dictionary<int, string> { [901] = "Zeta" },
            symbol, first, Material(700, "Aluminum"), Material(710, "Wood"), Material(720, "Brass"));
        Assert.Equal("[Brass|Wood|Aluminum]", MaterialOf(ifc, "IFCBUILDINGELEMENTPROXY", "100"));
    }

    [Fact]
    public void StyleCategoryWhoseNameIsUnknown_IsNamedAfterTheMaterial()
    {
        var geometry = InNodes(Solids(700, 710), solid => solid + 1);
        WithStyles(geometry, 801, 802);
        var symbol = Type(500, null, geometry);
        var first = Product(100, GenericModel, "OST_GenericModel", 500, Prism(_ => null));
        string ifc = Export(IfcProfile.Current,
            new Dictionary<int, (int, int)> { [801] = (0, 999), [802] = (0, 901) },
            new Dictionary<int, string> { [901] = "Alpha" },
            symbol, first, Material(700, "Aluminum"), Material(710, "Wood"));
        Assert.Equal("[Wood|Aluminum]", MaterialOf(ifc, "IFCBUILDINGELEMENTPROXY", "100"));
    }

    [Fact]
    public void FaceMaterial_WinsOverTheGraphicsStyleMaterial()
    {
        var geometry = InNodes(Solids(710), _ => 1);
        foreach (var face in geometry.Faces) face.GeometryStyle = 800;
        var symbol = Type(500, null, geometry);
        var product = Product(100, GenericModel, "OST_GenericModel", 500, Prism(_ => null));
        string ifc = Export(IfcProfile.Current, new Dictionary<int, (int, int)> { [800] = (700, 0) },
            symbol, product, Material(700, "Style material"), Material(710, "Face material"));
        Assert.Equal("Face material", MaterialOf(ifc, "IFCBUILDINGELEMENTPROXY", "100"));
    }

    [Theory]
    [InlineData(IfcProfile.Current, false)]
    [InlineData(IfcProfile.Legacy, true)]
    public void StairContainerAndWallSweep_HaveNoMaterialInCurrent(IfcProfile version, bool expectsMaterial)
    {
        // The stair container and a wall sweep carry no material association.
        var stair = Product(100, -2000120, "OST_Stairs", null, Prism(_ => 700));
        var sweep = Product(101, -2000181, "OST_Cornices", null, Prism(_ => 700));
        string ifc = Export(version, stair, sweep, Material(700, "Concrete"));
        Assert.Contains("=IFCSTAIR(", ifc);
        Assert.Equal(expectsMaterial, ifc.Contains("IFCRELASSOCIATESMATERIAL("));
    }

    [Fact]
    public void SolidWithoutAnyFaceMaterial_ContributesTheNullMaterial()
    {
        var symbol = Type(500, null, Solids(700, null));
        var product = Product(100, GenericModel, "OST_GenericModel", 500, Prism(_ => null));
        string ifc = Export(IfcProfile.Current, symbol, product, Material(700, "Task"));
        Assert.Equal("[<Unnamed>|Task]", MaterialOf(ifc, "IFCBUILDINGELEMENTPROXY", "100").Replace(" <", "<"));
    }

    [Theory]
    [InlineData(IfcProfile.Current, false)]
    [InlineData(IfcProfile.Legacy, true)]
    public void FoundationFamilyExportedAsSlab_HasNoMaterialOfItsOwnInCurrent(IfcProfile version, bool expectsMaterial)
    {
        var symbol = Type(500, null, Solids(700));
        var foundation = Product(100, -2001300, "OST_StructuralFoundation", 500, Prism(_ => null));
        string ifc = Export(version, symbol, foundation, Material(700, "Concrete"));
        var slab = Regex.Matches(ifc, @"#(\d+)=IFCSLAB\(").Select(m => int.Parse(m.Groups[1].Value)).Single();
        Assert.Equal(expectsMaterial, Associations(ifc).Any(a => a.Related.Contains(slab)));
    }

    [Theory]
    [InlineData(true, "System plastic")]
    [InlineData(false, "[Brass|Steel]")]
    public void PipeFitting_TakesTheMaterialOfItsSystemTypeBeforeItsFaces(bool systemHasMaterial, string expected)
    {
        var symbol = Type(500, null, Solids(710, 700));
        symbol.CategoryId = -2008049;
        var fitting = Product(100, -2008049, "OST_PipeFitting", 500, Prism(_ => null));
        fitting.Parameters.Add(new Parameter { Id = -1140334, Definition = new Definition { Name = "RBS_PIPING_SYSTEM_TYPE_PARAM" },
            StorageType = StorageType.ElementId, Value = 900 });
        var system = new Element { Id = 900, TypeId = -1, CategoryId = -2008099, Name = "Domestic cold water",
            Parameters = [new Parameter { Id = -1002107, Definition = new Definition { Name = "MATERIAL_ID_PARAM" },
                StorageType = StorageType.ElementId, Value = systemHasMaterial ? 720 : -1 }] };
        string ifc = Export(IfcProfile.Current, symbol, fitting, system,
            Material(700, "Steel"), Material(710, "Brass"), Material(720, "System plastic"));
        Assert.Equal(expected, MaterialOf(ifc, "IFCFLOWFITTING", "100"));
    }

    [Fact]
    public void MaterialRelation_GlobalIdIsTheMd5OfTheProjectAndTheFirstCreatedRelatedObject()
    {
        // Type #11 is created with its first instance #12 and so before it; instance #10 is older than both.
        var writer = new StepWriter();
        writer.AddWithId(1, "IFCPROJECT", "'PROJECTGUID0000000000',#5,'P',$,$,$,$,(#14),#11");
        writer.AddWithId(10, "IFCBUILDINGELEMENTPROXY", "'INSTANCE10GUID00000000',#5,'a',$,$,#2,#3,'10',$");
        writer.AddWithId(11, "IFCBUILDINGELEMENTPROXYTYPE", "'TYPE11GUID0000000000',#5,'t',$,$,$,$,'11',$,$");
        writer.AddWithId(12, "IFCBUILDINGELEMENTPROXY", "'INSTANCE12GUID00000000',#5,'b',$,$,#2,#3,'12',$");
        writer.AddWithId(20, "IFCRELDEFINESBYTYPE", "'REL',#5,$,$,(#12),#11");
        writer.AddWithId(30, "IFCRELASSOCIATESMATERIAL", "'OLD',#5,$,$,(#12,#11),#9");
        writer.AddWithId(31, "IFCRELASSOCIATESMATERIAL", "'OLDER',#5,$,$,(#12,#10),#9");
        Assert.Equal(2, IfcExporter.ApplyCurrentMaterialRelationGlobalIds(writer));
        string Guid(int index) => writer.EntityAt(index).Args.Split(',')[0].Trim('\'');
        Assert.Equal(IfcGuid.FromMd5("PROJECTGUID0000000000IfcRelAssociatesMaterial:TYPE11GUID0000000000"), Guid(5));
        Assert.Equal(IfcGuid.FromMd5("PROJECTGUID0000000000IfcRelAssociatesMaterial:INSTANCE10GUID00000000"), Guid(6));
    }

    [Fact]
    public void MaterialRelation_ExportedWithoutDiagnosticsStillGetsTheReferenceGlobalId()
    {
        var insulation = Product(100, PipeInsulations, "OST_PipeInsulations", null, Prism(_ => 700));
        string ifc = Export(IfcProfile.Current, insulation, Material(700, "Foam"));
        string project = Regex.Match(ifc, @"=IFCPROJECT\('([^']+)'").Groups[1].Value;
        string covering = Regex.Match(ifc, @"=IFCCOVERING\('([^']+)'").Groups[1].Value;
        string relation = Regex.Match(ifc, @"=IFCRELASSOCIATESMATERIAL\('([^']+)'").Groups[1].Value;
        Assert.Equal(IfcGuid.FromMd5($"{project}IfcRelAssociatesMaterial:{covering}"), relation);
    }

    [Fact]
    public void StructuralColumnSolidWithoutFaceMaterial_TakesTheStructuralMaterialOfItsType()
    {
        var symbol = Type(500, null, Solids((int?)null));
        symbol.CategoryId = -2001330;
        symbol.Parameters.Add(new Parameter { Id = -1005500, Definition = new Definition { Name = "STRUCTURAL_MATERIAL_PARAM" },
            StorageType = StorageType.ElementId, Value = 700 });
        var column = Product(100, -2001330, "OST_StructuralColumns", 500, Prism(_ => null));
        string ifc = Export(IfcProfile.Current, symbol, column, Material(700, "Concrete"));
        Assert.Equal("Concrete", MaterialOf(ifc, "IFCCOLUMN", "100"));
    }


    private static Element Material(int id, string name, bool shading = true) => new()
    {
        Id = id, TypeId = -1, IsBlockOnly = true, CategoryId = -2000700, Category = "OST_Materials", Name = name,
        HasShadingAppearance = shading,
        Parameters = shading
            ? [new Parameter { Id = id + 1, Definition = new Definition { Name = "MATERIAL_PARAM_COLOR" }, Value = 0x112233, StorageType = StorageType.Integer },
               new Parameter { Id = id + 2, Definition = new Definition { Name = "MATERIAL_PARAM_SHININESS" }, Value = 25, StorageType = StorageType.Integer }]
            : [],
    };

    /// <summary>A family symbol or type; <paramref name="materialParameter"/> is its Material parameter (-1002107).</summary>
    private static Element Type(int id, int? materialParameter, GeometryData? geometry = null) => new()
    {
        Id = id, TypeId = -1, Name = $"Type {id}", FamilyName = "Family", CategoryId = GenericModel, Geometry = geometry,
        Parameters = materialParameter is int material
            ? [new Parameter { Id = -1002107, Definition = new Definition { Name = "MATERIAL_ID_PARAM" },
                StorageType = StorageType.ElementId, Value = material }]
            : [],
    };

    private static Element Product(int id, int categoryId, string category, int? typeId, GeometryData geometry)
    {
        var parameters = new List<Parameter>
        {
            new() { Id = -1012100, StorageType = StorageType.ElementId, Value = 3 },
        };
        if (typeId is int type)
            parameters.Add(new Parameter { Id = -1002050, Definition = new Definition { Id = -1002050, Name = "ELEM_TYPE_PARAM" },
                StorageType = StorageType.ElementId, Value = type });
        return new Element
        {
            Id = id, TypeId = typeId ?? 0, SuperInstanceId = 999, IsDiscovered = true, CategoryId = categoryId, Category = category,
            Name = $"Product {id}", References = [3], Parameters = parameters,
            BoundingBox = [0, 0, 0, 4, 3, 2], Geometry = geometry,
        };
    }

    /// <summary>A 4 x 3 x 2 prism of six faces (0-1 are the 4 x 3 ends); <paramref name="style"/> gives each face's material.</summary>
    private static GeometryData Prism(Func<int, int?> style)
    {
        double[][] points = [[0, 0, 0], [4, 0, 0], [4, 3, 0], [0, 3, 0], [0, 0, 2], [4, 0, 2], [4, 3, 2], [0, 3, 2]];
        int[][] faces = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]];
        return new GeometryData
        {
            BoundingBox = [0, 0, 0, 4, 3, 2],
            Faces = faces.Select((indices, index) => new GeoFace
                { OuterVertices = indices.Select(i => points[i]).ToList(), RenderStyleId = style(index) }).ToList(),
            BrepSolids = [new GeoBrepSolid { IsClosed = true, FaceIndices = [0, 1, 2, 3, 4, 5] }],
        };
    }

    /// <summary>One solid of four equal faces per entry, every face carrying that entry's material (null: none).</summary>
    private static GeometryData Solids(params int?[] materials)
    {
        double[][] square = [[0, 0, 0], [1, 0, 0], [1, 1, 0], [0, 1, 0]];
        var geometry = new GeometryData { BrepSolids = [] };
        foreach (int? material in materials)
        {
            int start = geometry.Faces.Count;
            for (int i = 0; i < 4; i++)
                geometry.Faces.Add(new GeoFace { OuterVertices = square.ToList(), RenderStyleId = material });
            geometry.BrepSolids.Add(new GeoBrepSolid { IsClosed = true, FaceIndices = [start, start + 1, start + 2, start + 3] });
        }
        return geometry;
    }

    /// <summary>Assigns the Geometry node of every face of solid <c>i</c> to <paramref name="nodeOfSolid"/>(i)
    /// (a negative id marks a node that is not taken).</summary>
    private static GeometryData InNodes(GeometryData geometry, Func<int, int> nodeOfSolid)
    {
        for (int solid = 0; solid < geometry.BrepSolids!.Count; solid++)
            foreach (int face in geometry.BrepSolids[solid].FaceIndices)
                geometry.Faces[face].GeometryNode = nodeOfSolid(solid);
        return geometry;
    }

    // The graphics style of every face of each solid (0: none).
    private static void WithStyles(GeometryData geometry, params int[] styleOfSolid)
    {
        for (int solid = 0; solid < styleOfSolid.Length; solid++)
            foreach (int face in geometry.BrepSolids![solid].FaceIndices)
                geometry.Faces[face].GeometryStyle = styleOfSolid[solid];
    }

    private static string Export(IfcProfile version, params Element[] elements) =>
        Export(version, null, elements);

    private static string Export(IfcProfile version, Dictionary<int, (int, int)>? graphicsStyles, params Element[] elements) =>
        Export(version, graphicsStyles, null, elements);

    private static string Export(IfcProfile version, Dictionary<int, (int, int)>? graphicsStyles,
        Dictionary<int, string>? categoryNames, params Element[] elements)
    {
        var info = new RevitFileInfo { GraphicsStyles = graphicsStyles, CategoryNames = categoryNames };
        info.Elements.AddRange(elements);
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-mep-materials-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter { Profile = version }.Export(info, path);
            return File.ReadAllText(path);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>The material (see <see cref="Describe"/>) associated to the product of the class with the given Tag.</summary>
    private static string MaterialOf(string ifc, string ifcClass, string tag)
    {
        var product = Regex.Matches(ifc, $@"#(\d+)={ifcClass}\('[^']*',#\d+,'[^']*',[^,]*,[^,]*,#\d+,#\d+,'{tag}'")
            .Select(m => int.Parse(m.Groups[1].Value)).Single();
        return Describe(ifc, Associations(ifc).Single(a => a.Related.Contains(product)).Relating);
    }

    private static List<(List<int> Related, int Relating)> Associations(string ifc) =>
        Regex.Matches(ifc, @"=IFCRELASSOCIATESMATERIAL\('[^']*',#\d+,(?:'[^']*'|\$),(?:'[^']*'|\$),\(([^)]*)\),#(\d+)\);")
            .Select(m => (m.Groups[1].Value.Split(',').Select(x => int.Parse(x.TrimStart('#'))).ToList(),
                int.Parse(m.Groups[2].Value))).ToList();

    /// <summary>The name of an IfcMaterial, or "[a|b|c]" for an IfcMaterialList.</summary>
    private static string Describe(string ifc, int id)
    {
        var single = Regex.Match(ifc, $@"#{id}=IFCMATERIAL\('([^']*)'\);");
        if (single.Success) return single.Groups[1].Value;
        var list = Regex.Match(ifc, $@"#{id}=IFCMATERIALLIST\(\(([^)]*)\)\);");
        Assert.True(list.Success, $"#{id} is neither an IfcMaterial nor an IfcMaterialList");
        return "[" + string.Join('|', list.Groups[1].Value.Split(',').Select(x => Describe(ifc, int.Parse(x.TrimStart('#'))))) + "]";
    }
}
