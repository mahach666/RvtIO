using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The current profile: the sub-elements of a curtain wall are exported in one element call, so the body material state
/// (the last non-null best material) flows from one of them to the next (the curtain-wall export).</summary>
public class IfcExporterCurrentCurtainStyleTests
{
    private static readonly IReadOnlyDictionary<int, (GeometryData Geometry, int Node)> NoOrigins =
        new Dictionary<int, (GeometryData, int)>();

    private static IReadOnlyDictionary<int, int> NoNodes(GeometryData geometry) => new Dictionary<int, int>();

    // Material 700 has the surface style entity 7000, 710 the entity 7100; 720 has none.
    private static int StyleOf(int material) => material switch { 700 => 7000, 710 => 7100, _ => 0 };

    private static int Body(StepWriter w, int map = 0)
    {
        int representation;
        if (map == 0)
        {
            int brep = w.Add("IFCFACETEDBREP", "#1");
            representation = w.Add("IFCSHAPEREPRESENTATION", $"#9,'Body','Brep',(#{brep})");
        }
        else
        {
            int item = w.Add("IFCMAPPEDITEM", $"#{map},#6");
            representation = w.Add("IFCSHAPEREPRESENTATION", $"#9,'Body','MappedRepresentation',(#{item})");
        }
        int definition = w.Add("IFCPRODUCTDEFINITIONSHAPE", $"$,$,(#{representation})");
        return w.Add("IFCBUILDINGELEMENTPROXY", $"'GUID',#1,'P',$,$,#2,#{definition},'1',$");
    }

    private static int Map(StepWriter w)
    {
        int brep = w.Add("IFCFACETEDBREP", "#1");
        int representation = w.Add("IFCSHAPEREPRESENTATION", $"#9,'Body','Brep',(#{brep})");
        return w.Add("IFCREPRESENTATIONMAP", $"#5,#{representation}");
    }

    private static List<string> StyledItems(StepWriter w) =>
        Enumerable.Range(0, w.EntityCount).Select(w.EntityAt).Where(e => e.Type == "IFCSTYLEDITEM").Select(e => e.Args).ToList();

    private static IfcExporter.BodyStyleSources Sources(Action<IfcExporter.BodyStyleSources> fill)
    {
        var sources = new IfcExporter.BodyStyleSources();
        fill(sources);
        return sources;
    }

    [Fact]
    public void ProductWithoutAMaterial_TakesTheMaterialOfTheProductExportedBeforeIt()
    {
        var w = new StepWriter();
        int panel = Body(w), mullion = Body(w);
        var counts = IfcExporter.ApplyCurrentBodyStyles(w,
            Sources(s => { s.Solids[panel] = [700]; s.Solids[mullion] = [0]; s.Sequences.Add([panel, mullion]); }),
            NoOrigins, NoNodes, StyleOf);

        Assert.Equal(2, counts.StyledItems);
        Assert.Equal(1, counts.Assignments);   // one material twice: the second item reuses the assignment
    }

    [Fact]
    public void ProductExportedFirst_HasNoStateToTake()
    {
        var w = new StepWriter();
        int panel = Body(w), mullion = Body(w);
        var counts = IfcExporter.ApplyCurrentBodyStyles(w,
            Sources(s => { s.Solids[panel] = [700]; s.Solids[mullion] = [0]; s.Sequences.Add([mullion, panel]); }),
            NoOrigins, NoNodes, StyleOf);

        Assert.Equal(1, counts.StyledItems);
    }

    [Fact]
    public void State_IsEmptyAtTheStartOfEveryElementCall()
    {
        var w = new StepWriter();
        int panel = Body(w), mullion = Body(w), other = Body(w);
        var counts = IfcExporter.ApplyCurrentBodyStyles(w,
            Sources(s =>
            {
                s.Solids[panel] = [700]; s.Solids[mullion] = [0]; s.Solids[other] = [0];
                s.Sequences.Add([panel]);
                s.Sequences.Add([mullion]);   // another wall
            }),
            NoOrigins, NoNodes, StyleOf);

        Assert.Equal(1, counts.StyledItems);   // neither the second wall nor the product outside a sequence sees the first one's state
    }

    [Fact]
    public void State_IsTheLastMaterialSetNotTheFirst()
    {
        var w = new StepWriter();
        int first = Body(w), second = Body(w), mullion = Body(w);
        IfcExporter.ApplyCurrentBodyStyles(w,
            Sources(s =>
            {
                s.Solids[first] = [700]; s.Solids[second] = [710]; s.Solids[mullion] = [0];
                s.Sequences.Add([first, second, mullion]);
            }),
            NoOrigins, NoNodes, StyleOf);

        var styled = StyledItems(w);
        Assert.Equal(3, styled.Count);
        // the mullion is styled with the assignment of material 710 (the second product's), not of 700
        string assignment = styled[2].Split(',')[1].Trim('(', ')', '#');
        Assert.Equal("(#7100)", w.EntityAt(int.Parse(assignment) - 1).Args);
    }

    [Fact]
    public void ProductWhoseMapWasStyledBefore_DoesNotChangeTheState()
    {
        var w = new StepWriter();
        int map = Map(w);
        int creator = Body(w, map), user = Body(w, map), first = Body(w), mullion = Body(w);
        IfcExporter.ApplyCurrentBodyStyles(w,
            Sources(s =>
            {
                s.Solids[creator] = [700]; s.Solids[user] = [700]; s.Solids[first] = [710]; s.Solids[mullion] = [0];
                s.Sequences.Add([creator]);                   // creates the type of the map in another wall
                s.Sequences.Add([first, user, mullion]);      // the user runs no body export: 710 stays the state
            }),
            NoOrigins, NoNodes, StyleOf);

        var styled = StyledItems(w);
        Assert.Equal(3, styled.Count);   // creator's map item, first, mullion
        string assignment = styled[^1].Split(',')[1].Trim('(', ')', '#');
        Assert.Equal("(#7100)", w.EntityAt(int.Parse(assignment) - 1).Args);
    }

    [Fact]
    public void ProductsOfASequenceAreProcessedBeforeTheOthers()
    {
        // the first user of a map decides its style: the product of the sequence, although it is written later
        var w = new StepWriter();
        int map = Map(w);
        int outside = Body(w, map), inSequence = Body(w, map);
        IfcExporter.ApplyCurrentBodyStyles(w,
            Sources(s =>
            {
                s.Solids[outside] = [710]; s.Solids[inSequence] = [700];
                s.Sequences.Add([inSequence]);
            }),
            NoOrigins, NoNodes, StyleOf);

        var styled = StyledItems(w);
        Assert.Single(styled);
        string assignment = styled[0].Split(',')[1].Trim('(', ')', '#');
        Assert.Equal("(#7000)", w.EntityAt(int.Parse(assignment) - 1).Args);
    }


    private static int ExtrudedBody(StepWriter w, params string[] kinds)
    {
        var items = kinds.Select(kind => w.Add(kind, kind == "IFCEXTRUDEDAREASOLID" ? "$,#1,#2,1." : "#1")).ToList();
        int representation = w.Add("IFCSHAPEREPRESENTATION", $"#9,'Body','SolidModel',({string.Join(",", items.Select(item => "#" + item))})");
        int definition = w.Add("IFCPRODUCTDEFINITIONSHAPE", $"$,$,(#{representation})");
        return w.Add("IFCBUILDINGELEMENTPROXY", $"'GUID',#1,'P',$,$,#2,#{definition},'1',$");
    }

    // The surface style entity (7000, 7100) of the item written as the index-th entity of the given type; 0 when it has none.
    private static int StyleOfItem(StepWriter w, string kind, int index)
    {
        var items = Enumerable.Range(0, w.EntityCount).Select(w.EntityAt).Where(e => e.Type == kind).Select(e => e.Id).ToList();
        string reference = $"#{items[index]},(#";
        var styled = Enumerable.Range(0, w.EntityCount).Select(w.EntityAt)
            .FirstOrDefault(e => e.Type == "IFCSTYLEDITEM" && e.Args.StartsWith(reference, StringComparison.Ordinal));
        if (styled.Type == null) return 0;
        int assignment = int.Parse(styled.Args[reference.Length..].Split(')')[0]);
        string style = Enumerable.Range(0, w.EntityCount).Select(w.EntityAt).First(e => e.Id == assignment).Args;
        return int.Parse(style.Trim('(', ')', '#'));
    }

    [Fact]
    public void Extrusion_WithoutAMaterialTakesTheStateTheElementWasEnteredWith_NotTheMaterialOfALaterSolid()
    {
        // a window of a curtain wall: the frame has no material of its own, the glass has; the panel before it had 700
        var w = new StepWriter();
        int panel = Body(w), window = ExtrudedBody(w, "IFCEXTRUDEDAREASOLID", "IFCEXTRUDEDAREASOLID");
        IfcExporter.ApplyCurrentBodyStyles(w,
            Sources(s => { s.Solids[panel] = [700]; s.Solids[window] = [0, 710]; s.Sequences.Add([panel, window]); }),
            NoOrigins, NoNodes, StyleOf);

        Assert.Equal(7000, StyleOfItem(w, "IFCEXTRUDEDAREASOLID", 0));
        Assert.Equal(7100, StyleOfItem(w, "IFCEXTRUDEDAREASOLID", 1));
    }

    [Fact]
    public void Brep_WithoutAMaterialStillTakesTheLastMaterialOfTheElement()
    {
        var w = new StepWriter();
        int panel = Body(w), window = ExtrudedBody(w, "IFCFACETEDBREP", "IFCFACETEDBREP");
        IfcExporter.ApplyCurrentBodyStyles(w,
            Sources(s => { s.Solids[panel] = [700]; s.Solids[window] = [0, 710]; s.Sequences.Add([panel, window]); }),
            NoOrigins, NoNodes, StyleOf);

        Assert.Equal(7100, StyleOfItem(w, "IFCFACETEDBREP", 1));   // the window's own glass
        Assert.Equal(7100, StyleOfItem(w, "IFCFACETEDBREP", 2));
    }

    [Fact]
    public void Extrusion_FindingTheStateEmpty_TakesTheStateAtTheEndOfTheExtrusions()
    {
        var w = new StepWriter();
        int rods = ExtrudedBody(w, "IFCEXTRUDEDAREASOLID", "IFCEXTRUDEDAREASOLID", "IFCEXTRUDEDAREASOLID");
        IfcExporter.ApplyCurrentBodyStyles(w, Sources(s => s.Solids[rods] = [0, 0, 710]), NoOrigins, NoNodes, StyleOf);

        Assert.Equal([7100, 7100, 7100], Enumerable.Range(0, 3).Select(index => StyleOfItem(w, "IFCEXTRUDEDAREASOLID", index)));
    }

    [Fact]
    public void ExtrusionsAreStyledBeforeTheOtherSolidsSettleTheirMaterials()
    {
        // the Brep follows the extrusion in the product but is exported after it: the extrusion does not see its 710
        var w = new StepWriter();
        int panel = Body(w), part = ExtrudedBody(w, "IFCFACETEDBREP", "IFCEXTRUDEDAREASOLID");
        IfcExporter.ApplyCurrentBodyStyles(w,
            Sources(s => { s.Solids[panel] = [700]; s.Solids[part] = [710, 0]; s.Sequences.Add([panel, part]); }),
            NoOrigins, NoNodes, StyleOf);

        Assert.Equal(7000, StyleOfItem(w, "IFCEXTRUDEDAREASOLID", 0));
        Assert.Equal(7100, StyleOfItem(w, "IFCFACETEDBREP", 1));
    }

    [Fact]
    public void RoofSlab_TakesTheFirstLayerMaterialOfTheRoof()
    {
        var w = new StepWriter();
        int roof = w.Add("IFCROOF", "'GUID',#1,'R',$,$,#2,$,'1',.NOTDEFINED.");
        int slab = Body(w);
        var sources = Sources(s => s.Uniform[roof] = 700);
        IfcExporter.CopyUniformStyleSource(sources, roof, slab);

        Assert.Equal(700, sources.Uniform[slab]);
        Assert.Equal(1, IfcExporter.ApplyCurrentBodyStyles(w, sources, NoOrigins, NoNodes, StyleOf).StyledItems);

        // a roof without a first layer material leaves the slab as it was
        var empty = Sources(_ => { });
        IfcExporter.CopyUniformStyleSource(empty, roof, slab);
        Assert.False(empty.Uniform.ContainsKey(slab));
    }


    private static Element CurtainChild(int id, int categoryId, string category, int? materialId, double x)
    {
        double[][] corners = [[0, 0, 0], [1, 0, 0], [1, 1, 0], [0, 1, 0], [0, 0, 1], [1, 0, 1], [1, 1, 1], [0, 1, 1]];
        int[][] rings = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]];
        return new Element
        {
            Id = id, SuperInstanceId = 999, IsDiscovered = true, CategoryId = categoryId, Category = category, Name = $"Product {id}",
            References = [3], SerializedCurtainHostId = 100,
            Parameters = [new Parameter { Id = -1012100, StorageType = StorageType.ElementId, Value = 3 }],
            BoundingBox = [x, 0, 0, x + 1, 1, 1],
            Geometry = new GeometryData
            {
                BoundingBox = [x, 0, 0, x + 1, 1, 1],
                Faces = rings.Select(ring => new GeoFace
                {
                    OuterVertices = ring.Select(index => new[] { corners[index][0] + x, corners[index][1], corners[index][2] }).ToList(),
                    RenderStyleId = materialId, GeometryNode = 1,
                }).ToList(),
                BrepSolids = [new GeoBrepSolid { IsClosed = true, FaceIndices = [0, 1, 2, 3, 4, 5] }],
            },
        };
    }

    [Theory]
    [InlineData(IfcProfile.Current, 2)]
    [InlineData(IfcProfile.Legacy, 0)]
    public void Export_ABodyWithoutAMaterialInACurtainWallTakesThePanelMaterial(IfcProfile version, int styledBreps)
    {
        var glass = new Element
        {
            Id = 700, TypeId = -1, IsBlockOnly = true, CategoryId = -2000700, Category = "OST_Materials", Name = "Glass",
            HasShadingAppearance = true,
            Parameters =
            [
                new Parameter { Id = 701, Definition = new Definition { Name = "MATERIAL_PARAM_COLOR" }, Value = 0x336699, StorageType = StorageType.Integer },
                new Parameter { Id = 702, Definition = new Definition { Name = "MATERIAL_PARAM_SHININESS" }, Value = 25, StorageType = StorageType.Integer },
                new Parameter { Id = 703, Definition = new Definition { Name = "MATERIAL_PARAM_TRANSPARENCY" }, Value = 70, StorageType = StorageType.Integer },
            ],
        };
        var type = new Element { Id = 500, Name = "Curtain type", FamilyName = "Curtain family", CategoryId = -2000011, SerializedCurtainWallFunction = 1 };
        var wall = new Element
        {
            Id = 100, TypeId = 500, SuperInstanceId = 999, IsDiscovered = true, CategoryId = -2000011, Category = "OST_Walls", Name = "Wall",
            References = [3],
            Parameters =
            [
                new Parameter { Id = -1012100, StorageType = StorageType.ElementId, Value = 3 },
                new Parameter { Id = -1002050, Definition = new Definition { Id = -1002050, Name = "ELEM_TYPE_PARAM" }, StorageType = StorageType.ElementId, Value = 500 },
            ],
        };
        // The mullion has the lower element id, so it is written first; the expected export exports the panels of the wall before its mullions
        // inside the call of the wall, and the mullion takes the last material set in that call.
        var mullion = CurtainChild(101, -2000171, "OST_CurtainWallMullions", null, 3);
        var panel = CurtainChild(102, -2000170, "OST_CurtainWallPanels", 700, 0);
        var info = new RevitFileInfo();
        info.Elements.AddRange([type, wall, mullion, panel, glass]);
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-curtain-{Guid.NewGuid():N}.ifc");
        string ifc;
        try
        {
            new IfcExporter { Profile = version }.Export(info, path);
            ifc = File.ReadAllText(path);
        }
        finally { if (File.Exists(path)) File.Delete(path); }

        var breps = Regex.Matches(ifc, @"#(\d+)=IFCFACETEDBREP\(").Select(match => match.Groups[1].Value).ToHashSet();
        Assert.Equal(2, breps.Count);
        var styled = Regex.Matches(ifc, @"=IFCSTYLEDITEM\(#(\d+),\(#(\d+)\),''\);").Where(match => breps.Contains(match.Groups[1].Value)).ToList();
        Assert.Equal(styledBreps, styled.Count);
        if (styledBreps == 2)
            Assert.Single(styled.Select(match => match.Groups[2].Value).Distinct());   // both take the one assignment of the material
    }


    [Theory]
    [InlineData(IfcProfile.Current, true)]
    [InlineData(IfcProfile.Legacy, false)]
    public void Export_StylesARoomBodyWithoutAMaterialWithTheRoomInteriorsStyle(IfcProfile version, bool styled)
    {
        var level = Storey(900, "Level", 0.0);
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
                    { OuterVertices = ring.Select(index => (double[])corners[index].Clone()).ToList(), GeometryNode = 1 }).ToList(),
                BrepSolids = [new GeoBrepSolid { IsClosed = true, FaceIndices = [0, 1, 2, 3, 4, 5] }],
            },
        };
        string ifc = ExportRoom(version, level, room);

        var brep = Regex.Match(ifc, @"#(\d+)=(?:IFCFACETEDBREP|IFCEXTRUDEDAREASOLID)\(").Groups[1].Value;
        Assert.NotEmpty(brep);
        Assert.Equal(styled, Regex.IsMatch(ifc, $@"=IFCSTYLEDITEM\(#{brep},\(#\d+\),''\);"));
        if (!styled) return;
        Assert.Contains("=IFCSURFACESTYLE('Room interiors',.BOTH.,", ifc);
        Assert.Matches(@"=IFCCOLOURRGB\('',0\.85098\d*,0\.95294\d*,0\.99215\d*\);", ifc);
        Assert.Matches(@"=IFCSURFACESTYLERENDERING\(#\d+,0\.75,\$,\$,\$,\$,IFCNORMALISEDRATIOMEASURE\(0\.5\),IFCSPECULAREXPONENT\(64\.\),\.NOTDEFINED\.\);", ifc);
    }

    private static string ExportRoom(IfcProfile version, params Element[] elements)
    {
        var info = new RevitFileInfo();
        foreach (var element in elements) info.Elements.Add(element);
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-room-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter { Profile = version }.Export(info, path);
            return File.ReadAllText(path);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }


    private static Element FourBoxes()
    {
        // The tops slope (no pair of parallel faces), so the solids stay Breps: a vertical box is a SweptSolid in the current profile.
        double[][] corners = [[0, 0, 0], [4, 0, 0], [4, 3, 0], [0, 3, 0], [0, 0, 2], [4, 0, 2], [4, 3, 3], [0, 3, 3]];
        int[][] rings = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]];
        int[] nodes = [1, 1, 2, -3];   // two disjoint shells in node 1, one in node 2, one the exporter does not take
        var faces = new List<GeoFace>();
        var solids = new List<GeoBrepSolid>();
        for (int box = 0; box < nodes.Length; box++)
        {
            solids.Add(new GeoBrepSolid { IsClosed = true, FaceIndices = Enumerable.Range(faces.Count, 6).ToList() });
            faces.AddRange(rings.Select(ring => new GeoFace
            {
                OuterVertices = ring.Select(index => new[] { corners[index][0] + 10 * box, corners[index][1], corners[index][2] }).ToList(),
                GeometryNode = nodes[box],
            }));
        }
        return new Element
        {
            Id = 100, TypeId = 0, SuperInstanceId = 999, IsDiscovered = true, CategoryId = -2000151, Category = "OST_GenericModel",
            Name = "Product 100", References = [3],
            Parameters = [new Parameter { Id = -1012100, StorageType = StorageType.ElementId, Value = 3 }],
            BoundingBox = [0, 0, 0, 34, 3, 2],
            Geometry = new GeometryData { BoundingBox = [0, 0, 0, 34, 3, 2], Faces = faces, BrepSolids = solids },
        };
    }

    private static string ExportProduct(IfcProfile version, Element product)
    {
        var info = new RevitFileInfo();
        info.Elements.Add(product);
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-nodes-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter { Profile = version }.Export(info, path);
            return File.ReadAllText(path);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ExportCurrent_WritesOneBrepPerGeometryNodeAndLeavesOutTheNodesItDoesNotTake()
    {
        string ifc = ExportProduct(IfcProfile.Current, FourBoxes());

        var shells = Regex.Matches(ifc, @"=IFCCLOSEDSHELL\(\(([^)]*)\)\);").Select(match => match.Groups[1].Value.Split(',').Length).ToList();
        Assert.Equal(2, Regex.Matches(ifc, @"=IFCFACETEDBREP\(").Count);
        Assert.Equal([12, 6], shells);   // the two shells of node 1 in one closed shell, then node 2; the untaken node is gone
    }

    [Fact]
    public void ExportCurrent_ASolidThatReusesAFaceOfAnEarlierNode_IsTheItemOfItsOwnNode()
    {
        var product = FourBoxes();
        var geometry = product.Geometry!;
        geometry.Faces = geometry.Faces.Take(12).ToList();   // two boxes
        geometry.BrepSolids = geometry.BrepSolids!.Take(2).ToList();
        foreach (var face in geometry.Faces.Skip(6)) face.GeometryNode = 2;
        // the first face of the second solid is a plane the solid shares with the first one: it was seen under node 1
        geometry.Faces[6].GeometryNode = 1;

        string ifc = ExportProduct(IfcProfile.Current, product);

        var shells = Regex.Matches(ifc, @"=IFCCLOSEDSHELL\(\(([^)]*)\)\);").Select(match => match.Groups[1].Value.Split(',').Length).ToList();
        Assert.Equal([6, 6], shells);   // one item per node, not one item for the two solids
    }

    [Fact]
    public void ExportLegacy_KeepsOneBrepPerClosedShellIncludingTheUntakenNodes()
    {
        string ifc = ExportProduct(IfcProfile.Legacy, FourBoxes());

        Assert.Equal(4, Regex.Matches(ifc, @"=IFCFACETEDBREP\(").Count);
    }

    [Fact]
    public void ExportCurrent_KeepsAGeometryWithoutAnyTakenNode()
    {
        var product = FourBoxes();
        foreach (var face in product.Geometry!.Faces) face.GeometryNode = -Math.Abs(face.GeometryNode);

        string ifc = ExportProduct(IfcProfile.Current, product);

        Assert.Equal(4, Regex.Matches(ifc, @"=IFCFACETEDBREP\(").Count);   // nothing is taken: every face stays, as before
    }

    [Fact]
    public void ExportCurrent_KeepsOneBrepPerShellForAGeometryThatHasGhostNodes()
    {
        var product = FourBoxes();
        product.Geometry!.GhostNodes = [(5, -1, 0)];   // a node the expected export exports that left no polygon here

        string ifc = ExportProduct(IfcProfile.Current, product);

        Assert.Equal(4, Regex.Matches(ifc, @"=IFCFACETEDBREP\(").Count);   // as before: every shell, the untaken node too
    }


    private static Element GridLine(int id, string label, double x0, double y0, double x1, double y1, double? low, double? high, bool discovered = false) => new()
    {
        Id = id, CategoryId = -2000220, Category = "OST_Grids", Name = label, IsDiscovered = discovered,
        Parameters = [new Parameter { Id = -1008000, StorageType = StorageType.String, Value = label }],
        Location = new LocationCurve { CurveType = "Line", StartPoint = [x0, y0, 4.0], EndPoint = [x1, y1, 4.0] },
        SerializedGridLevelInterval = low == null ? null : [low.Value, high!.Value],
    };

    private static Element Storey(int id, string name, double elevationFeet) => new()
    {
        Id = id, TypeId = 1001, CategoryId = -2000240, Category = "OST_Levels", Name = name, IsNativeLevel = true,
        SerializedFamilyId = -1, SerializedUnplacedOwnerId = -1, SerializedOwnerDbViewId = -1,
        Parameters =
        [
            new Parameter { Id = -1007102, Definition = new Definition { Name = "LEVEL_ELEV" }, StorageType = StorageType.Double, Value = elevationFeet },
            new Parameter { Id = -1007111, StorageType = StorageType.Integer, Value = 1 },
        ],
    };

    private static string ExportGrids(bool exactElemTable, params Element[] elements)
    {
        var info = new RevitFileInfo { ElemTableIsExact = exactElemTable };
        foreach (var element in elements) info.Elements.Add(element);
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-grids-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter { Profile = IfcProfile.Current }.Export(info, path);
            return File.ReadAllText(path);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static Element[] Square(double? low, double? high) =>
    [
        GridLine(10, "A", 0, 0, 10, 0, low, high), GridLine(11, "B", 0, 5, 10, 5, low, high),
        GridLine(12, "1", 0, 0, 0, 5, low, high), GridLine(13, "2", 5, 0, 5, 5, low, high),
    ];

    [Fact]
    public void GridLinesOnLevel_KeepsTheLinesWhoseIntervalReachesTheElevation()
    {
        var lines = IfcExporter.CollectGridLines(
        [
            GridLine(1, "A", 0, 0, 10, 0, -1, 5), GridLine(2, "B", 0, 1, 10, 1, 8, 20),
            GridLine(3, "C", 0, 2, 10, 2, null, null),
        ]);

        Assert.Equal([1], IfcExporter.GridLinesOnLevel(lines, 0).Select(line => line.Element.Id));
        Assert.Equal([2], IfcExporter.GridLinesOnLevel(lines, 10).Select(line => line.Element.Id));
        Assert.Equal([1], IfcExporter.GridLinesOnLevel(lines, 5 + 1e-10).Select(line => line.Element.Id));   // within 1e-9
        Assert.Empty(IfcExporter.GridLinesOnLevel(lines, 6));
    }

    [Fact]
    public void Export_PutsAGridOnlyOnTheLevelsItsIntervalReaches()
    {
        Element[] levels = [Storey(1, "Ground", 0.0), Storey(2, "Upper", 10.0)];

        string both = ExportGrids(true, [.. levels, .. Square(-1, 20)]);
        string ground = ExportGrids(true, [.. levels, .. Square(-1, 5)]);
        string none = ExportGrids(true, [.. levels, .. Square(30, 40)]);

        Assert.Equal(2, Regex.Matches(both, @"=IFCGRID\(").Count);
        Assert.Equal(8, Regex.Matches(both, @"=IFCGRIDAXIS\(").Count);
        Assert.Single(Regex.Matches(ground, @"=IFCGRID\("));
        Assert.Equal(4, Regex.Matches(ground, @"=IFCGRIDAXIS\(").Count);
        Assert.Empty(Regex.Matches(none, @"=IFCGRID\("));
    }

    [Fact]
    public void Export_DropsAGridRecoveredOutsideTheExactElemTable()
    {
        Element[] levels = [Storey(1, "Ground", 0.0)];
        Element[] ghosts = [GridLine(20, "X", 0, 0, 10, 10, -1, 5, discovered: true), GridLine(21, "Y", 0, 10, 10, 0, -1, 5, discovered: true)];

        Assert.Single(Regex.Matches(ExportGrids(true, [.. levels, .. Square(-1, 5)]), @"=IFCGRID\("));
        // with the exact table the ghosts are no grid and the lines that remain form the one rectangular grid
        string exact = ExportGrids(true, [.. levels, .. Square(-1, 5), .. ghosts]);
        Assert.Single(Regex.Matches(exact, @"=IFCGRID\("));
        Assert.Equal(4, Regex.Matches(exact, @"=IFCGRIDAXIS\(").Count);
        // a record without an ElemTable row is no grid whether or not the table decodes exactly (the expected export walks the table)
        string loose = ExportGrids(false, [.. levels, .. Square(-1, 5), .. ghosts]);
        Assert.Equal(4, Regex.Matches(loose, @"=IFCGRIDAXIS\(").Count);
    }


    private static Element Panel(int id, double x, double bottom, int category = -2000170) => new()
    {
        Id = id, CategoryId = category, BoundingBox = [x, 0, bottom, x + 3, 0.2, bottom + 2],
    };

    private static Element Wall(double x0, double y0, double x1, double y1) => new()
    {
        Id = 1, CategoryId = -2000011,
        Location = new LocationCurve { CurveType = "Line", StartPoint = [x0, y0, 0], EndPoint = [x1, y1, 0] },
    };

    [Fact]
    public void CurtainExportOrder_PanelsRowByRowAlongTheWallThenMullionsById()
    {
        var children = new[]
        {
            Panel(40, 3, 0), Panel(10, 6, 2.5), Panel(30, 0, 2.5), Panel(20, 0, 0.1),
            Panel(60, 1, 0, -2000171), Panel(50, 2, 0, -2000171),
        };
        var order = IfcExporter.CurtainExportOrderCurrent(Wall(0, 0.1, 10, 0.1), children);

        // row 0: x = 0 (bottom 0.1), 3 (bottom 0); row 1: x = 0, 6; then the mullions
        Assert.Equal([20, 40, 30, 10, 50, 60], order);
    }

    [Fact]
    public void CurtainExportOrder_FollowsTheDirectionOfTheLocationLine()
    {
        var children = new[] { Panel(10, 0, 0), Panel(20, 3, 0), Panel(30, 6, 0) };

        Assert.Equal([30, 20, 10], IfcExporter.CurtainExportOrderCurrent(Wall(10, 0, 0, 0), children));
        Assert.Equal([10, 20, 30], IfcExporter.CurtainExportOrderCurrent(Wall(0, 0, 10, 0), children));
    }

    [Fact]
    public void CurtainExportOrder_KeepsTheIdOrderWithoutALocationLineOrWithoutBounds()
    {
        var noLine = new Element { Id = 1, CategoryId = -2000035 };
        Assert.Equal([10, 20, 30, 5], IfcExporter.CurtainExportOrderCurrent(noLine,
            [Panel(30, 0, 0), Panel(10, 6, 0), Panel(20, 3, 3), Panel(5, 1, 1, -2000171)]));

        var unbounded = new Element { Id = 20, CategoryId = -2000170 };
        Assert.Equal([10, 20, 30], IfcExporter.CurtainExportOrderCurrent(Wall(0, 0, 10, 0),
            [Panel(30, 0, 0), unbounded, Panel(10, 6, 0)]));
    }
}
