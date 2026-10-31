using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>Presentation styles of the current profile: the curve style of the grid axes and the surface style of the Body items.</summary>
public class IfcExporterCurrentStyleTests
{
    private static StepWriter Writer(params (string Type, string Args)[] entities)
    {
        var w = new StepWriter();
        foreach (var (type, args) in entities) w.Add(type, args);
        return w;
    }

    [Fact]
    public void GridCurveStyle_IsABlackColourACurveStyleWithoutFontOrWidthAnAssignmentAndTheStyledCurveSet()
    {
        var w = Writer(("IFCGEOMETRICCURVESET", "(#5)"));
        IfcExporter.AddGridCurveStyle(w, 1);

        Assert.Equal(5, w.EntityCount);
        Assert.Equal(("IFCCOLOURRGB", "$,0.,0.,0."), (w.EntityAt(1).Type, w.EntityAt(1).Args));
        Assert.Equal(("IFCCURVESTYLE", "'',$,$,#2"), (w.EntityAt(2).Type, w.EntityAt(2).Args));
        Assert.Equal(("IFCPRESENTATIONSTYLEASSIGNMENT", "(#3)"), (w.EntityAt(3).Type, w.EntityAt(3).Args));
        Assert.Equal(("IFCSTYLEDITEM", "#1,(#4),$"), (w.EntityAt(4).Type, w.EntityAt(4).Args));
    }

    private static Element Grid(int id, string label, double x0, double y0, double x1, double y1) => new()
    {
        Id = id, CategoryId = -2000220, Category = "OST_Grids", Name = "6.5mm Bubble",
        Parameters = [new Parameter { Id = -1008000, StorageType = StorageType.String, Value = label }],
        Location = new LocationCurve { CurveType = "Line", StartPoint = [x0, y0, 4.0], EndPoint = [x1, y1, 4.0] },
    };

    private static Element Level(int id, string name, double elevationFeet) => new()
    {
        Id = id, TypeId = 1001, CategoryId = -2000240, Category = "OST_Levels", Name = name, IsNativeLevel = true,
        SerializedFamilyId = -1, SerializedUnplacedOwnerId = -1, SerializedOwnerDbViewId = -1,
        Parameters =
        [
            new Parameter { Id = -1007102, Definition = new Definition { Name = "LEVEL_ELEV" }, StorageType = StorageType.Double, Value = elevationFeet },
            new Parameter { Id = -1007111, StorageType = StorageType.Integer, Value = 1 },
        ],
    };

    private static string Export(IfcProfile version, params Element[] elements)
    {
        var info = new RevitFileInfo();
        foreach (var element in elements) info.Elements.Add(element);
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-styles-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter { Profile = version }.Export(info, path);
            return File.ReadAllText(path);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static Element[] GridModel() =>
    [
        Level(1, "Ground", 0.0), Level(2, "Upper", 10.0),
        Grid(10, "A", 0, 0, 10, 0), Grid(11, "B", 0, 5, 10, 5), Grid(12, "1", 0, 0, 0, 5), Grid(13, "2", 5, 0, 5, 5),
    ];

    [Fact]
    public void ExportCurrent_EveryGridAxisCurveSetHasABlackCurveStyle()
    {
        string ifc = Export(IfcProfile.Current, GridModel());

        int axes = Regex.Matches(ifc, @"IFCGRIDAXIS\(").Count;
        Assert.Equal(8, axes);
        Assert.Equal(axes, Regex.Matches(ifc, @"=IFCCURVESTYLE\('',\$,\$,#\d+\);").Count);
        Assert.Equal(axes, Regex.Matches(ifc, @"=IFCCOLOURRGB\('',0\.,0\.,0\.\);").Count);
        foreach (Match curveSet in Regex.Matches(ifc, @"#(\d+)=IFCGEOMETRICCURVESET\("))
            Assert.Matches($@"=IFCSTYLEDITEM\(#{curveSet.Groups[1].Value},\(#\d+\),''\);", ifc);
    }

    [Fact]
    public void ExportLegacy_GridAxesHaveNoCurveStyle()
    {
        string ifc = Export(IfcProfile.Legacy, GridModel());

        Assert.Contains("IFCGRIDAXIS(", ifc);
        Assert.DoesNotContain("IFCCURVESTYLE(", ifc);
    }

    private const string Proxy = "'GUID',#1,'P',$,$,#2,#3,'1',$";

    private static (StepWriter Writer, int Product, int[] Items) OneBody(params string[] itemTypes)
    {
        var w = new StepWriter();
        int[] items = itemTypes.Select(type => w.Add(type, "#1")).ToArray();
        int representation = w.Add("IFCSHAPEREPRESENTATION", $"#9,'Body','Brep',({string.Join(",", items.Select(i => "#" + i))})");
        int definition = w.Add("IFCPRODUCTDEFINITIONSHAPE", $"$,$,(#{representation})");
        int product = w.Add("IFCBUILDINGELEMENTPROXY", $"'GUID',#1,'P',$,$,#2,#{definition},'1',$");
        return (w, product, items);
    }

    private static IfcExporter.BodyStyleSources Sources(Action<IfcExporter.BodyStyleSources> fill)
    {
        var sources = new IfcExporter.BodyStyleSources();
        fill(sources);
        return sources;
    }

    private static readonly IReadOnlyDictionary<int, (GeometryData Geometry, int Node)> NoOrigins =
        new Dictionary<int, (GeometryData, int)>();

    private static IReadOnlyDictionary<int, int> NoNodes(GeometryData geometry) => new Dictionary<int, int>();

    // Material 700 has the surface style entity 7000, material 710 the entity 7100; 720 has none.
    private static int StyleOf(int material) => material switch { 700 => 7000, 710 => 7100, _ => 0 };

    private static List<string> StyledItems(StepWriter w) =>
        Enumerable.Range(0, w.EntityCount).Select(w.EntityAt).Where(e => e.Type == "IFCSTYLEDITEM").Select(e => e.Args).ToList();

    [Fact]
    public void HostObject_StylesEveryBodyItemWithTheMaterialOfItsFirstLayer()
    {
        var (w, product, items) = OneBody("IFCEXTRUDEDAREASOLID", "IFCFACETEDBREP");
        var counts = IfcExporter.ApplyCurrentBodyStyles(w, Sources(s => s.Uniform[product] = 700), NoOrigins, NoNodes, StyleOf);

        Assert.Equal(new IfcExporter.BodyStyleCounts(1, 2, 1, 0), counts);
        // one assignment of the cache serves the first and the second item of a material
        var assignment = w.EntityAt(w.EntityCount - 3);
        Assert.Equal(("IFCPRESENTATIONSTYLEASSIGNMENT", "(#7000)"), (assignment.Type, assignment.Args));
        Assert.Equal([$"#{items[0]},(#{assignment.Id}),$", $"#{items[1]},(#{assignment.Id}),$"], StyledItems(w));
    }

    [Fact]
    public void PresentationStyleAssignment_IsCreatedForTheFirstUseReusedForTheSecondAndCreatedAgainForTheThird()
    {
        var (w, product, items) = OneBody("IFCFACETEDBREP", "IFCFACETEDBREP", "IFCFACETEDBREP", "IFCFACETEDBREP", "IFCFACETEDBREP");
        var counts = IfcExporter.ApplyCurrentBodyStyles(w, Sources(s => s.Uniform[product] = 700), NoOrigins, NoNodes, StyleOf);

        Assert.Equal(5, counts.StyledItems);
        Assert.Equal(3, counts.Assignments);
        var assignments = StyledItems(w).Select(args => args.Split(',')[1]).ToList();
        Assert.Equal(assignments[0], assignments[1]);
        Assert.Equal(assignments[2], assignments[3]);
        Assert.NotEqual(assignments[1], assignments[2]);
        Assert.NotEqual(assignments[3], assignments[4]);
    }

    [Fact]
    public void SolidsOfDifferentMaterials_StyleTheItemsInOrderWhenTheirNumberAgrees()
    {
        var (w, product, items) = OneBody("IFCFACETEDBREP", "IFCFACETEDBREP");
        IfcExporter.ApplyCurrentBodyStyles(w, Sources(s => s.Solids[product] = [710, 700]), NoOrigins, NoNodes, StyleOf);

        var styled = StyledItems(w);
        var assignmentOf = (int item) => styled.Single(args => args.StartsWith($"#{item},")).Split(',')[1].Trim('(', ')', '#');
        Assert.Contains("(#7100)", w.EntityAt(int.Parse(assignmentOf(items[0])) - 1).Args);
        Assert.Contains("(#7000)", w.EntityAt(int.Parse(assignmentOf(items[1])) - 1).Args);
    }

    [Fact]
    public void SolidsOfDifferentMaterials_LeaveTheItemsUnstyledWhenTheirNumberDiffers()
    {
        var (w, product, _) = OneBody("IFCFACETEDBREP", "IFCFACETEDBREP", "IFCFACETEDBREP");
        int before = w.EntityCount;
        var counts = IfcExporter.ApplyCurrentBodyStyles(w, Sources(s => s.Solids[product] = [710, 700]), NoOrigins, NoNodes, StyleOf);

        Assert.Equal(new IfcExporter.BodyStyleCounts(0, 0, 0, 3), counts);
        Assert.Equal(before, w.EntityCount);
    }

    [Fact]
    public void SolidsOfOneMaterial_StyleAnyNumberOfItems()
    {
        var (w, product, _) = OneBody("IFCFACETEDBREP", "IFCFACETEDBREP", "IFCFACETEDBREP");
        var counts = IfcExporter.ApplyCurrentBodyStyles(w, Sources(s => s.Solids[product] = [700, 700]), NoOrigins, NoNodes, StyleOf);

        Assert.Equal(3, counts.StyledItems);
    }

    [Fact]
    public void ItemWrittenFromAGeometryNode_TakesTheMaterialOfThatNode()
    {
        var (w, product, items) = OneBody("IFCFACETEDBREP", "IFCFACETEDBREP", "IFCFACETEDBREP");
        var geometry = new GeometryData();
        var origins = new Dictionary<int, (GeometryData, int)> { [items[0]] = (geometry, 2), [items[1]] = (geometry, 1), [items[2]] = (geometry, 2) };
        var sources = Sources(s =>
        {
            s.Solids[product] = [700, 710];
            s.Rules[product] = (null, null);
        });
        var counts = IfcExporter.ApplyCurrentBodyStyles(w, sources, origins,
            g => ReferenceEquals(g, geometry) ? new Dictionary<int, int> { [1] = 700, [2] = 710 } : new Dictionary<int, int>(), StyleOf);

        // three items for two solids: the nodes, not the counts, decide
        Assert.Equal(new IfcExporter.BodyStyleCounts(1, 3, 2, 0), counts);
        var styled = StyledItems(w);
        string StyleEntityOf(int item) =>
            w.EntityAt(int.Parse(styled.Single(args => args.StartsWith($"#{item},")).Split(',')[1].Trim('(', ')', '#')) - 1).Args;
        Assert.Equal("(#7100)", StyleEntityOf(items[0]));
        Assert.Equal("(#7000)", StyleEntityOf(items[1]));
        Assert.Equal("(#7100)", StyleEntityOf(items[2]));
    }

    [Fact]
    public void SystemMaterial_ReplacesTheMaterialOfEveryNode_AndTheStructuralOneOnlyTheNullMaterial()
    {
        var (w, product, items) = OneBody("IFCFACETEDBREP", "IFCFACETEDBREP");
        var geometry = new GeometryData();
        var origins = new Dictionary<int, (GeometryData, int)> { [items[0]] = (geometry, 1), [items[1]] = (geometry, 2) };
        IReadOnlyDictionary<int, int> Nodes(GeometryData g) => new Dictionary<int, int> { [1] = 0, [2] = 710 };
        string StyleOfItem(StepWriter writer, int item)
        {
            var styled = StyledItems(writer).Single(args => args.StartsWith($"#{item},"));
            return writer.EntityAt(int.Parse(styled.Split(',')[1].Trim('(', ')', '#')) - 1).Args;
        }

        IfcExporter.ApplyCurrentBodyStyles(w, Sources(s => s.Rules[product] = (700, null)), origins, Nodes, StyleOf);
        Assert.Equal("(#7000)", StyleOfItem(w, items[0]));
        Assert.Equal("(#7000)", StyleOfItem(w, items[1]));

        var (w2, product2, items2) = OneBody("IFCFACETEDBREP", "IFCFACETEDBREP");
        var origins2 = new Dictionary<int, (GeometryData, int)> { [items2[0]] = (geometry, 1), [items2[1]] = (geometry, 2) };
        IfcExporter.ApplyCurrentBodyStyles(w2, Sources(s => s.Rules[product2] = (null, 700)), origins2, Nodes, StyleOf);
        Assert.Equal("(#7000)", StyleOfItem(w2, items2[0]));   // the null material takes the structural one
        Assert.Equal("(#7100)", StyleOfItem(w2, items2[1]));   // a material of the node stays

        var (w3, product3, items3) = OneBody("IFCFACETEDBREP", "IFCFACETEDBREP");
        var origins3 = new Dictionary<int, (GeometryData, int)> { [items3[0]] = (geometry, 1), [items3[1]] = (geometry, 2) };
        var counts = IfcExporter.ApplyCurrentBodyStyles(w3, Sources(s => s.Rules[product3] = (null, null)), origins3, Nodes, StyleOf);
        Assert.Equal(2, counts.StyledItems);                  // the null node takes the last material of the element (710)
        var (w4, product4, items4) = OneBody("IFCFACETEDBREP");
        var origins4 = new Dictionary<int, (GeometryData, int)> { [items4[0]] = (geometry, 1) };
        Assert.Equal(0, IfcExporter.ApplyCurrentBodyStyles(w4, Sources(s => s.Rules[product4] = (null, null)), origins4, Nodes, StyleOf).StyledItems);
    }

    [Fact]
    public void ItemWithoutAMaterial_TakesTheLastMaterialOfTheElementWhereverItStands()
    {
        var (w, product, items) = OneBody("IFCFACETEDBREP", "IFCFACETEDBREP", "IFCFACETEDBREP", "IFCFACETEDBREP");
        var geometry = new GeometryData();
        var origins = items.Select((item, index) => (item, index)).ToDictionary(pair => pair.item, pair => (geometry, pair.index + 1));
        var sources = Sources(s => s.Rules[product] = (null, null));
        var counts = IfcExporter.ApplyCurrentBodyStyles(w, sources, origins,
            _ => new Dictionary<int, int> { [1] = 0, [2] = 700, [3] = 0, [4] = 710 }, StyleOf);

        Assert.Equal(4, counts.StyledItems);
        var styled = StyledItems(w);
        string StyleEntityOf(int item) =>
            w.EntityAt(int.Parse(styled.Single(args => args.StartsWith($"#{item},")).Split(',')[1].Trim('(', ')', '#')) - 1).Args;
        Assert.Equal("(#7000)", StyleEntityOf(items[1]));   // its own material stays
        Assert.Equal("(#7100)", StyleEntityOf(items[0]));   // the last material of the element, 710, though it comes after
        Assert.Equal("(#7100)", StyleEntityOf(items[2]));
    }

    [Fact]
    public void ItemWithoutAMaterial_StaysUnstyledWhenTheElementHasNone()
    {
        var geometry = new GeometryData();
        var (w2, product2, items2) = OneBody("IFCFACETEDBREP", "IFCFACETEDBREP");
        var origins2 = items2.Select((item, index) => (item, index)).ToDictionary(pair => pair.item, pair => (geometry, pair.index + 1));
        var none = IfcExporter.ApplyCurrentBodyStyles(w2, Sources(s => s.Rules[product2] = (null, null)), origins2,
            _ => new Dictionary<int, int> { [1] = 0, [2] = 0 }, StyleOf);
        Assert.Equal(0, none.StyledItems);
    }

    [Fact]
    public void HostWithoutAFirstLayerMaterial_IsStyledLikeAnyBodyFromTheMaterialsOfItsNodes()
    {
        var (w, product, items) = OneBody("IFCFACETEDBREP");
        var geometry = new GeometryData();
        var origins = new Dictionary<int, (GeometryData, int)> { [items[0]] = (geometry, 1) };
        var counts = IfcExporter.ApplyCurrentBodyStyles(w, Sources(s => s.Uniform[product] = 0), origins,
            _ => new Dictionary<int, int> { [1] = 710 }, StyleOf);

        Assert.Equal(1, counts.StyledItems);
        Assert.Equal("(#7100)", w.EntityAt(w.EntityCount - 2).Args);

        // an extruded item has no node: the null first layer leaves it unstyled
        var (w2, product2, _) = OneBody("IFCEXTRUDEDAREASOLID");
        Assert.Equal(0, IfcExporter.ApplyCurrentBodyStyles(w2, Sources(s => s.Uniform[product2] = 0), NoOrigins, NoNodes, StyleOf).StyledItems);
    }

    [Fact]
    public void MapWhoseFirstUserCouldNotTellTheMaterial_IsLeftToTheNextUser()
    {
        var w = new StepWriter();
        int brep = w.Add("IFCFACETEDBREP", "#1");
        int mappedRepresentation = w.Add("IFCSHAPEREPRESENTATION", $"#9,'Body','Brep',(#{brep})");
        int map = w.Add("IFCREPRESENTATIONMAP", $"#5,#{mappedRepresentation}");
        int product(string tag)
        {
            int item = w.Add("IFCMAPPEDITEM", $"#{map},#6");
            int representation = w.Add("IFCSHAPEREPRESENTATION", $"#9,'Body','MappedRepresentation',(#{item})");
            int definition = w.Add("IFCPRODUCTDEFINITIONSHAPE", $"$,$,(#{representation})");
            return w.Add("IFCBUILDINGELEMENTPROXY", $"'GUID',#1,'P',$,$,#2,#{definition},'{tag}',$");
        }
        int first = product("1"), second = product("2");
        // the first user has two solids of different materials for one item and so cannot tell; the second has one
        var counts = IfcExporter.ApplyCurrentBodyStyles(w,
            Sources(s => { s.Solids[first] = [700, 710]; s.Solids[second] = [710]; }), NoOrigins, NoNodes, StyleOf);

        Assert.Equal(1, counts.StyledItems);
        Assert.Equal("(#7100)", w.EntityAt(w.EntityCount - 2).Args);
    }

    [Fact]
    public void ItemWithoutAnOrigin_FallsBackToTheSolidsOfTheProduct()
    {
        var (w, product, items) = OneBody("IFCFACETEDBREP", "IFCFACETEDBREP");
        var geometry = new GeometryData();
        var origins = new Dictionary<int, (GeometryData, int)> { [items[0]] = (geometry, 1) };
        var sources = Sources(s =>
        {
            s.Solids[product] = [700, 710];
            s.Rules[product] = (null, null);
        });
        var counts = IfcExporter.ApplyCurrentBodyStyles(w, sources, origins,
            _ => new Dictionary<int, int> { [1] = 710 }, StyleOf);

        // item 0 follows its node (710); item 1 has none and takes the solid at its index (710)
        Assert.Equal(2, counts.StyledItems);
        Assert.Equal(1, counts.Assignments);   // one material twice: the second use reuses the assignment
    }

    [Fact]
    public void NullMaterialAndMaterialWithoutAStyle_LeaveTheItemUnstyled()
    {
        var (w, product, _) = OneBody("IFCFACETEDBREP", "IFCFACETEDBREP");
        int before = w.EntityCount;
        IfcExporter.ApplyCurrentBodyStyles(w, Sources(s => s.Solids[product] = [0, 720]), NoOrigins, NoNodes, StyleOf);

        Assert.Equal(before, w.EntityCount);
    }

    [Fact]
    public void OpeningElementsAndNonStyleableItems_AreNotStyled()
    {
        var w = new StepWriter();
        int brep = w.Add("IFCFACETEDBREP", "#1");
        int box = w.Add("IFCBOUNDINGBOX", "#1,1.,1.,1.");
        int representation = w.Add("IFCSHAPEREPRESENTATION", $"#9,'Body','Brep',(#{brep})");
        int boxRepresentation = w.Add("IFCSHAPEREPRESENTATION", $"#9,'Box','BoundingBox',(#{box})");
        int definition = w.Add("IFCPRODUCTDEFINITIONSHAPE", $"$,$,(#{representation},#{boxRepresentation})");
        int opening = w.Add("IFCOPENINGELEMENT", $"'GUID',#1,'O',$,$,#2,#{definition},'1'");
        int wall = w.Add("IFCWALL", $"'GUID',#1,'W',$,$,#2,#{definition},'2'");
        var counts = IfcExporter.ApplyCurrentBodyStyles(w,
            Sources(s => { s.Uniform[opening] = 700; s.Uniform[wall] = 700; }), NoOrigins, NoNodes, StyleOf);

        // the wall shares the item with the opening: the item is styled once, the box never
        Assert.Equal(1, counts.StyledItems);
        Assert.DoesNotContain(StyledItems(w), args => args.StartsWith($"#{box},"));
    }

    [Fact]
    public void MappedItems_AreStyledOnceThroughTheirRepresentationMap()
    {
        var w = new StepWriter();
        int brep = w.Add("IFCFACETEDBREP", "#1");
        int mappedRepresentation = w.Add("IFCSHAPEREPRESENTATION", $"#9,'Body','Brep',(#{brep})");
        int map = w.Add("IFCREPRESENTATIONMAP", $"#5,#{mappedRepresentation}");
        int product(string tag)
        {
            int item = w.Add("IFCMAPPEDITEM", $"#{map},#6");
            int representation = w.Add("IFCSHAPEREPRESENTATION", $"#9,'Body','MappedRepresentation',(#{item})");
            int definition = w.Add("IFCPRODUCTDEFINITIONSHAPE", $"$,$,(#{representation})");
            return w.Add("IFCBUILDINGELEMENTPROXY", $"'GUID',#1,'P',$,$,#2,#{definition},'{tag}',$");
        }
        int first = product("1"), second = product("2");
        var counts = IfcExporter.ApplyCurrentBodyStyles(w,
            Sources(s => { s.Solids[first] = [700]; s.Solids[second] = [710]; }), NoOrigins, NoNodes, StyleOf);

        // the type takes the material of its first instance
        Assert.Equal(new IfcExporter.BodyStyleCounts(1, 1, 1, 0), counts);
        Assert.Equal("(#7000)", w.EntityAt(w.EntityCount - 2).Args);
    }

    [Theory]
    [InlineData(IfcProfile.Current, true)]
    [InlineData(IfcProfile.Legacy, false)]
    public void Export_StylesTheBoundaryBodyOfASpaceByTheMaterialOfItsFaces(IfcProfile version, bool styled)
    {
        var material = new Element
        {
            Id = 700, TypeId = -1, IsBlockOnly = true, CategoryId = -2000700, Category = "OST_Materials", Name = "Room interiors",
            HasShadingAppearance = true,
            Parameters =
            [
                new Parameter { Id = 701, Definition = new Definition { Name = "MATERIAL_PARAM_COLOR" }, Value = 0x336699, StorageType = StorageType.Integer },
                new Parameter { Id = 702, Definition = new Definition { Name = "MATERIAL_PARAM_SHININESS" }, Value = 25, StorageType = StorageType.Integer },
                new Parameter { Id = 703, Definition = new Definition { Name = "MATERIAL_PARAM_TRANSPARENCY" }, Value = 75, StorageType = StorageType.Integer },
            ],
        };
        var level = new Element
        {
            Id = 900, Name = "Level", IsNativeLevel = true, SerializedFamilyId = -1, SerializedUnplacedOwnerId = -1, SerializedOwnerDbViewId = -1,
            Parameters =
            [
                new Parameter { Id = -1007102, Definition = new Definition { Name = "LEVEL_ELEV" }, StorageType = StorageType.Double, Value = 0.0 },
                new Parameter { Id = -1007111, StorageType = StorageType.Integer, Value = 1 },
            ],
        };
        double[][] corners = [[0, 0, 0], [1, 0, 0], [1, 1, 0], [0, 1, 0], [0, 0, 1], [1, 0, 1], [1, 1, 1], [0, 1, 1]];
        int[][] rings = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]];
        var room = new Element
        {
            Id = 100, CategoryId = -2000160, Category = "OST_Rooms", Name = "Room",
            Parameters =
            [
                new Parameter { Id = -1006916, StorageType = StorageType.ElementId, Value = 900 },
                new Parameter { Id = -1006901, StorageType = StorageType.String, Value = "1" },
            ],
            SerializedSpaceBoundary = new GeometryData
            {
                Faces = rings.Select(ring => new GeoFace
                    { OuterVertices = ring.Select(index => (double[])corners[index].Clone()).ToList(), RenderStyleId = 700, GeometryNode = 1 }).ToList(),
                BrepSolids = [new GeoBrepSolid { IsClosed = true, FaceIndices = [0, 1, 2, 3, 4, 5] }],
            },
        };
        string ifc = Export(version, level, room, material);

        string body = Regex.Match(ifc, @"=IFCSPACE\('[^']*',#\d+,'1',[^,]*,[^,]*,#\d+,#(\d+),").Groups[1].Value;
        Assert.NotEmpty(body);
        var brep = Regex.Match(ifc, @"#(\d+)=(?:IFCFACETEDBREP|IFCEXTRUDEDAREASOLID)\(").Groups[1].Value;
        Assert.NotEmpty(brep);
        Assert.Equal(styled, Regex.IsMatch(ifc, $@"=IFCSTYLEDITEM\(#{brep},\(#\d+\),''\);"));
        if (styled) Assert.Contains("=IFCSURFACESTYLE('Room interiors',.BOTH.,", ifc);
    }

    private static Element ShadedMaterial(int id, string name, int color) => new()
    {
        Id = id, TypeId = -1, IsBlockOnly = true, CategoryId = -2000700, Category = "OST_Materials", Name = name,
        HasShadingAppearance = true,
        Parameters =
        [
            new Parameter { Id = id + 1, Definition = new Definition { Name = "MATERIAL_PARAM_COLOR" }, Value = color, StorageType = StorageType.Integer },
            new Parameter { Id = id + 2, Definition = new Definition { Name = "MATERIAL_PARAM_SHININESS" }, Value = 25, StorageType = StorageType.Integer },
            new Parameter { Id = id + 3, Definition = new Definition { Name = "MATERIAL_PARAM_TRANSPARENCY" }, Value = 0, StorageType = StorageType.Integer },
        ],
    };

    /// <summary>The faces of an axis-aligned box and the closed solid of its six faces, added to a geometry; every face carries the Geometry node, its place in the walk order and the material given.</summary>
    private static void AddBox(GeometryData geometry, double[] min, double[] max, int node, long order, int material)
    {
        double[] P(int x, int y, int z) => [x == 0 ? min[0] : max[0], y == 0 ? min[1] : max[1], z == 0 ? min[2] : max[2]];
        int[][][] sides =
        [
            [[0, 0, 0], [0, 1, 0], [1, 1, 0], [1, 0, 0]], [[0, 0, 1], [1, 0, 1], [1, 1, 1], [0, 1, 1]],
            [[0, 0, 0], [1, 0, 0], [1, 0, 1], [0, 0, 1]], [[1, 0, 0], [1, 1, 0], [1, 1, 1], [1, 0, 1]],
            [[1, 1, 0], [0, 1, 0], [0, 1, 1], [1, 1, 1]], [[0, 1, 0], [0, 0, 0], [0, 0, 1], [0, 1, 1]],
        ];
        var indices = new List<int>();
        foreach (var side in sides)
        {
            indices.Add(geometry.Faces.Count);
            geometry.Faces.Add(new GeoFace
            {
                OuterVertices = side.Select(corner => P(corner[0], corner[1], corner[2])).ToList(),
                RenderStyleId = material, GeometryNode = node, NodeOrder = order,
            });
        }
        geometry.BrepSolids ??= [];
        geometry.BrepSolids.Add(new GeoBrepSolid { IsClosed = true, FaceIndices = indices });
    }

    private static Element ProductOf(GeometryData geometry) => new()
    {
        Id = 100, TypeId = 0, SuperInstanceId = 999, IsDiscovered = true, CategoryId = -2000151, Category = "OST_GenericModel",
        Name = "Product 100", References = [3],
        Parameters = [new Parameter { Id = -1012100, StorageType = StorageType.ElementId, Value = 3 }],
        BoundingBox = [0, 0, 0, 20, 5, 5], Geometry = geometry,
    };

    /// <summary>The surface style name of every faceted brep of the Body representation of the exported product, in the order
    /// of the representation ("-" for an unstyled one).</summary>
    private static List<string> BodyItemStyles(string ifc)
    {
        var entities = Regex.Matches(ifc, @"(?m)^#(\d+)=(\w+)\((.*)\);\r?$")
            .ToDictionary(match => int.Parse(match.Groups[1].Value), match => (Type: match.Groups[2].Value, Args: match.Groups[3].Value));
        string body = entities.Values.First(e => e.Type == "IFCSHAPEREPRESENTATION" && e.Args.Contains("'Body'")).Args;
        var result = new List<string>();
        foreach (int item in Regex.Matches(body, @"#(\d+)").Select(match => int.Parse(match.Groups[1].Value))
                     .Where(id => entities.TryGetValue(id, out var entity) && entity.Type is "IFCFACETEDBREP" or "IFCEXTRUDEDAREASOLID"))
        {
            string styledArgs = entities.Values.FirstOrDefault(e => e.Type == "IFCSTYLEDITEM" && e.Args.StartsWith($"#{item},")).Args;
            var assignment = Regex.Match(styledArgs ?? "", @"\(#(\d+)\)");
            if (!assignment.Success) { result.Add("-"); continue; }
            int surfaceStyle = int.Parse(Regex.Match(entities[int.Parse(assignment.Groups[1].Value)].Args, @"#(\d+)").Groups[1].Value);
            result.Add(Regex.Match(entities[surfaceStyle].Args, @"^'([^']*)'").Groups[1].Value);
        }
        return result;
    }

    [Theory]
    [InlineData(IfcProfile.Current, @"0\.97500002384185\d*")]
    [InlineData(IfcProfile.Legacy, @"0\.97")]
    public void Export_WritesTheStoredFractionOfAMaterialTransparencyOnlyInCurrent(IfcProfile version, string expected)
    {
        var geometry = new GeometryData { BoundingBox = [0, 0, 0, 20, 5, 5], Faces = [] };
        AddBox(geometry, [0, 0, 0], [4, 4, 4], node: 1, order: 1L << 42, material: 700);
        var material = ShadedMaterial(700, "Epoxy", 0x336699);
        material.Parameters.Single(parameter => parameter.Id == 703).Value = 97;
        material.SerializedMaterialTransparency = 0.975f;
        string ifc = Export(version, ProductOf(geometry), material);

        Assert.Matches($@"=IFCSURFACESTYLERENDERING\(#\d+,{expected},", ifc);
    }

    [Fact]
    public void ExportCurrent_ListsTheBodyItemsInTheOrderOfReferencesNodeWalk()
    {
        var geometry = new GeometryData { BoundingBox = [0, 0, 0, 20, 5, 5], Faces = [] };
        AddBox(geometry, [10, 0, 0], [14, 4, 4], node: 1, order: 2L << 42, material: 710);
        AddBox(geometry, [0, 0, 0], [4, 4, 4], node: 2, order: 1L << 42, material: 700);
        string ifc = Export(IfcProfile.Current, ProductOf(geometry), ShadedMaterial(700, "Brass", 0x336699), ShadedMaterial(710, "Steel", 0x999999));

        Assert.Equal(["Brass", "Steel"], BodyItemStyles(ifc));
    }

    [Theory]
    [InlineData(IfcProfile.Current, true)]
    [InlineData(IfcProfile.Legacy, false)]
    public void Export_StylesTheBodyOfAProductByTheMaterialOfItsSolids(IfcProfile version, bool styled)
    {
        var material = new Element
        {
            Id = 700, TypeId = -1, IsBlockOnly = true, CategoryId = -2000700, Category = "OST_Materials", Name = "Brass",
            HasShadingAppearance = true,
            Parameters =
            [
                new Parameter { Id = 701, Definition = new Definition { Name = "MATERIAL_PARAM_COLOR" }, Value = 0x336699, StorageType = StorageType.Integer },
                new Parameter { Id = 702, Definition = new Definition { Name = "MATERIAL_PARAM_SHININESS" }, Value = 25, StorageType = StorageType.Integer },
                new Parameter { Id = 703, Definition = new Definition { Name = "MATERIAL_PARAM_TRANSPARENCY" }, Value = 40, StorageType = StorageType.Integer },
            ],
        };
        double[][] points = [[0, 0, 0], [4, 0, 0], [4, 3, 0], [0, 3, 0], [0, 0, 2], [4, 0, 2], [4, 3, 2], [0, 3, 2]];
        int[][] faces = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]];
        var geometry = new GeometryData
        {
            BoundingBox = [0, 0, 0, 4, 3, 2],
            Faces = faces.Select(indices => new GeoFace
                { OuterVertices = indices.Select(i => points[i]).ToList(), RenderStyleId = 700, GeometryNode = 1 }).ToList(),
            BrepSolids = [new GeoBrepSolid { IsClosed = true, FaceIndices = [0, 1, 2, 3, 4, 5] }],
        };
        var product = new Element
        {
            Id = 100, TypeId = 0, SuperInstanceId = 999, IsDiscovered = true, CategoryId = -2000151, Category = "OST_GenericModel",
            Name = "Product 100", References = [3],
            Parameters = [new Parameter { Id = -1012100, StorageType = StorageType.ElementId, Value = 3 }],
            BoundingBox = [0, 0, 0, 4, 3, 2], Geometry = geometry,
        };
        string ifc = Export(version, product, material);

        // The current profile writes the closed vertical box as an extruded solid, the legacy profile as a Brep: both are Body items that take a style.
        var breps = Regex.Matches(ifc, @"#(\d+)=(?:IFCFACETEDBREP|IFCEXTRUDEDAREASOLID)\(").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.NotEmpty(breps);
        var styledBreps = Regex.Matches(ifc, @"=IFCSTYLEDITEM\(#(\d+),\(#(\d+)\),''\);")
            .Where(m => breps.Contains(m.Groups[1].Value)).ToList();
        Assert.True((styled ? 1 : 0) == styledBreps.Count, string.Join("\n", ifc.Split('\n').Where(line => Regex.IsMatch(line,
            "STYLE|BREP|PROXY|SHAPEREP|PRODUCTDEF|MATERIAL"))));
        if (!styled) return;
        Assert.Matches($@"#{styledBreps[0].Groups[2].Value}=IFCPRESENTATIONSTYLEASSIGNMENT\(\(#\d+\)\);", ifc);
        Assert.Contains("=IFCSURFACESTYLE('Brass',.BOTH.,", ifc);
        Assert.Contains("IFCSURFACESTYLERENDERING(#", ifc);
        Assert.Contains("=IFCCOLOURRGB('',0.6,0.4,0.2);", ifc);
    }
}
