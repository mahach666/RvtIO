using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class OwnWallPlanarBodyTests
{
    [Fact]
    public void InactiveClassifierFaceKeepsItsNodeAndNormalWithoutAddingGeometry()
    {
        var source = Cube();
        source.InactiveFaces = [new() { SourceFace = 77, GeometryNode = 9, XVector = [1, 1, 1], CurvedSurface = true }];

        var geometry = ElementBuilder.BuildGeometryData(source)!;

        Assert.Equal(6, geometry.Faces.Count);
        Assert.Equal(6, Assert.Single(geometry.BrepSolids!).FaceIndices.Count);
        var inactive = Assert.Single(geometry.InactiveFaces!);
        Assert.Equal(77u, inactive.SourceFace);
        Assert.Equal(9, inactive.GeometryNode);
        Assert.Equal(new double[] { 1, 1, 1 }, inactive.Normal);
        Assert.True(inactive.CurvedSurface);
        Assert.Null(inactive.OuterVertices);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CurrentCutPairBodyRequiresCompleteSourceGroupsAndClearsOnRebinding(bool missingGroup)
    {
        var body = Cube();
        body.Faces = body.Faces.Select((face, index) => face with
            { SourceFace = (uint)index + 1, FirstSourceLoop = face.Vertices }).ToList();
        var state = new ParsingState();
        state.OwnedElementClassRoots[42] = new(1,3524);
        state.OwnWallPlanarBodies[42] = (1,body,null);
        state.OwnCurrentWallFaceGroups[42] = Enumerable.Range(1,missingGroup ? 5 : 6)
            .ToDictionary(i=>(uint)i,i=>(uint)i);
        var wall = new Element { Id=42,CategoryId=-2000011 };
        Assert.True(ElementBuilder.ApplyOwnWallPlanarBody(wall,state));
        Assert.Equal(!missingGroup,wall.OwnCurrentCutPairBodyCurrent!=null);
        if (!missingGroup) Assert.NotNull(wall.OwnCurrentCutPairBodyCurrent!.Faces[0].FirstSourceLoop);
        Assert.False(ElementBuilder.ApplyOwnWallPlanarBody(wall,new()));
        Assert.Null(wall.OwnCurrentCutPairBodyCurrent);
    }

    internal static GRepData Cube()
    {
        double[][] p = [[0,0,0], [1,0,0], [1,1,0], [0,1,0],
            [0,0,1], [1,0,1], [1,1,1], [0,1,1]];
        int[][] rings = [[0,3,2,1], [4,5,6,7], [0,1,5,4], [3,7,6,2], [0,4,7,3], [1,2,6,5]];
        return new() { PreserveSourceFaceIdentity = true, BoundingBox = [0,0,0,1,1,1],
            Faces = rings.Select(r => new GRepFace { Vertices = r.Select(i => p[i]).ToList() }).ToList(),
            Solids = [new() { IsClosed = true, FaceIndices = [0,1,2,3,4,5] }] };
    }

    [Theory]
    [InlineData(null)] [InlineData("duplicate-root")] [InlineData("wrong-root")]
    [InlineData("duplicate-grep")] [InlineData("missing-grep")] [InlineData("unconfirmed")]
    [InlineData("nan")] [InlineData("flat")] [InlineData("open-solid")]
    [InlineData("missing-face")] [InlineData("duplicate-face")] [InlineData("bad-shell")]
    [InlineData("wrong-category")] [InlineData("block-only")] [InlineData("invalid-index")]
    public void BindingRequiresOwnUniqueRootCompleteCoverageAndClosedWriterShell(string? mode)
    {
        var state = new ParsingState(); var body = Cube();
        state.OwnedElementClassRoots[42] = new(1, 3524);
        state.OwnWallPlanarBodies[42] = (1, body, null);
        var legacy = new GeometryData { GStyleElemId = 77, Flags = 123 };
        var wall = new Element { Id = 42, CategoryId = -2000011, Geometry = legacy };
        string before = System.Text.Json.JsonSerializer.Serialize(wall);
        switch (mode)
        {
            case "duplicate-root": state.OwnedElementClassRoots[42] = new(2, 3524); break;
            case "wrong-root": state.OwnedElementClassRoots[42] = new(1, 105); break;
            case "duplicate-grep": state.OwnWallPlanarBodies[42] = (2, body, null); break;
            case "missing-grep": state.OwnWallPlanarBodies.Clear(); break;
            case "unconfirmed": body.PreserveSourceFaceIdentity = false; break;
            case "nan": body.BoundingBox![0] = double.NaN; break;
            case "flat": body.BoundingBox![5] = 0; break;
            case "open-solid": body.Solids[0].IsClosed = false; break;
            case "missing-face": body.Solids[0].FaceIndices.RemoveAt(5); break;
            case "duplicate-face": body.Solids[0].FaceIndices.Add(0); break;
            case "invalid-index": body.Solids[0].FaceIndices[0] = 99; break;
            case "bad-shell": body.Faces[0].Vertices![0] = [0,0,0.25]; break;
            case "wrong-category": wall.CategoryId = -2000038; break;
            case "block-only": wall.IsBlockOnly = true; break;
        }
        Assert.Equal(mode == null, ElementBuilder.ApplyOwnWallPlanarBody(wall, state));
        Assert.Same(legacy, wall.Geometry);
        if (mode != null) { Assert.Null(wall.OwnPrimaryPlanarBody); return; }
        Assert.NotNull(wall.OwnPrimaryPlanarBody);
        Assert.Equal(6, wall.OwnPrimaryPlanarBody.Faces.Count);
        Assert.Equal(77, wall.OwnPrimaryPlanarBody.GStyleElemId); Assert.Equal(123u, wall.OwnPrimaryPlanarBody.Flags);
        Assert.Equal(body.BoundingBox, wall.OwnPrimaryPlanarBody.BoundingBox);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(wall));
        Assert.False(ElementBuilder.ApplyOwnWallPlanarBody(wall, new ParsingState()));
        Assert.Null(wall.OwnPrimaryPlanarBody);
    }

    [Fact]
    public void SourceIdentityDoesNotMergeEqualCoordinateFaces()
    {
        var body = Cube(); body.Faces.Add(body.Faces[0]); body.Solids[0].FaceIndices.Add(6);
        Assert.Equal(7, ElementBuilder.BuildGeometryData(body)!.Faces.Count);
        body.PreserveSourceFaceIdentity = false;
        Assert.Equal(6, ElementBuilder.BuildGeometryData(body)!.Faces.Count);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UnsupportedAndMalformedGrepDuplicatesNeverRescueEachOther(bool reverse)
    {
        var state = new ParsingState();
        foreach (ushort cls in reverse ? new ushort[] { 2006, 2007 } : new ushort[] { 2007, 2006 })
            RvtFileReader.RecordOwnWallPlanarBody(state, [], 0, 0, cls, 42);
        Assert.Equal(2, state.OwnWallPlanarBodies[42].BlockCount);
        Assert.Null(state.OwnWallPlanarBodies[42].Body);
        Assert.Equal("duplicate-grep", state.OwnWallPlanarBodies[42].Failure);
        RvtFileReader.RecordOwnWallPlanarBody(state, [], 0, 0, 105, 42);
        Assert.Equal(2, state.OwnWallPlanarBodies[42].BlockCount);
        Assert.Empty(new ParsingState().OwnWallPlanarBodies);
    }

    [Fact]
    public void APlanarFaceWithoutOwnDirectBodyIsNotAdmitted()
    {
        Assert.Null(GRepParserV2.SelectOwnFirstDirectPlanarBody(
            GElementPointWithinFirstSampleTests.Graph(), out var failure));
        Assert.NotNull(failure);
    }

    [Fact]
    public void StandardCaseRetainsItsAxisAndSweptContractUntilSourceAxisIsConfirmed()
    {
        var state = new ParsingState();
        state.OwnedElementClassRoots[42] = new(1, 3524);
        state.OwnWallPlanarBodies[42] = (1, Cube(), null);
        var wall = new Element { Id = 42, CategoryId = -2000011, Category = "OST_Walls",
            Name = "Wall", TypeId = 99,
            BoundingBox = [0,0,0,1,1,1], Geometry = ElementBuilder.BuildGeometryData(Cube()),
            Location = new LocationCurve { CurveType = "Line", StartPoint = [0,0,0], EndPoint = [1,0,0] },
            Parameters = [new() { Id = -1001005, StorageType = StorageType.Double, Value = 10d }] };
        Assert.True(ElementBuilder.ApplyOwnWallPlanarBody(wall, state));
        var model = new RevitFileInfo(); model.Elements.Add(wall);
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-own-body-{Guid.NewGuid():N}.ifc");
        try
        {
            new RvtIO.Ifc.IfcExporter().Export(model, path);
            string ifc = File.ReadAllText(path);
            Assert.Contains("=IFCWALLSTANDARDCASE(", ifc);
            Assert.Contains("=IFCEXTRUDEDAREASOLID(", ifc);
            Assert.DoesNotContain("=IFCFACETEDBREP(", ifc);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(null)] [InlineData("void")] [InlineData("standard-case")]
    [InlineData("duplicate")] [InlineData("no-validation")]
    public void DeferredWriterRequiresTheCompleteNoVoidProductContract(string? mode)
    {
        var wall = new Element { Id = 42, CategoryId = -2000011 };
        var state = new ParsingState();
        state.OwnedElementClassRoots[42] = new(1,3524);
        state.OwnWallPlanarBodies[42] = (1,Cube(),null);
        Assert.True(ElementBuilder.ApplyOwnWallPlanarBody(wall,state));
        if (mode == "no-validation") wall.OwnPrimaryPlanarBody = null;
        var writer = new RvtIO.Ifc.StepWriter();
        int product = writer.Add(mode == "standard-case" ? "IFCWALLSTANDARDCASE" : "IFCWALL", "'g',#1,'n',$,$,#2,#3,'42'");
        if (mode == "void") writer.Add("IFCRELVOIDSELEMENT", $"'v',#1,$,$,#{product},#99");
        string before = writer.EntityAt(0).Args; int count = writer.EntityCount;
        Assert.Equal(mode == null ? 1 : 0, new RvtIO.Ifc.IfcExporter().ApplyOwnWallPlanarBodies(
            writer, new Dictionary<int,Element> { [42] = wall },
            new Dictionary<int,int> { [42] = mode == "duplicate" ? 2 : 1 },
            new Dictionary<int,int> { [product] = 42 }, 1,2,3));
        if (mode == null) { Assert.NotEqual(before,writer.EntityAt(0).Args); Assert.True(writer.EntityCount>count); }
        else { Assert.Equal(before,writer.EntityAt(0).Args); Assert.Equal(count,writer.EntityCount); }
    }

    [Theory]
    [InlineData(false, 1)] [InlineData(true, 0)]
    public void ANodeReferenceWritesAsTriangles_KeepsItsFaceMeshesInCurrent(bool withNode, int replaced)
    {
        var state = new ParsingState(); state.OwnedElementClassRoots[42] = new(1,3524);
        state.OwnWallPlanarBodies[42] = (1,Cube(),null);
        var wall = new Element { Id = 42, CategoryId = -2000011 };
        Assert.True(ElementBuilder.ApplyOwnWallPlanarBody(wall,state));
        wall.Geometry = new GeometryData { MeshNodes = withNode ? [new GeometryNode(1, 0, [], 1.0)] : null };
        var writer = new RvtIO.Ifc.StepWriter();
        int product = writer.Add("IFCWALL", "'g',#1,'n',$,$,#2,#3,'42'");
        Assert.Equal(replaced,new RvtIO.Ifc.IfcExporter { Profile = RvtIO.Ifc.IfcProfile.Current }.ApplyOwnWallPlanarBodies(
            writer, new Dictionary<int,Element> { [42] = wall },new Dictionary<int,int> { [42] = 1 },
            new Dictionary<int,int> { [product] = 42 },1,2,3));
    }

    [Fact]
    public void InvalidWrittenVoidIsNotSilentlyTreatedAsNoOperand()
    {
        var writer = new RvtIO.Ifc.StepWriter();
        writer.Add("IFCRELVOIDSELEMENT", "'v',#1,$,$,$,#99");
        Assert.Throws<InvalidOperationException>(() => new RvtIO.Ifc.IfcExporter().ApplyOwnWallPlanarBodies(
            writer, new Dictionary<int,Element>(), new Dictionary<int,int>(),new Dictionary<int,int>(),1,2,3));
        Assert.Equal(1,writer.EntityCount);
    }

    [Fact]
    public void DeferredBodyRetainsCoordinatesFinerThanTheWriterLattice()
    {
        var body = Cube();
        foreach (var point in body.Faces.SelectMany(f => f.Vertices!).Distinct()) point[0] += 0.00001;
        body.BoundingBox![0] += 0.00001; body.BoundingBox[3] += 0.00001;
        var state = new ParsingState(); state.OwnedElementClassRoots[42] = new(1,3524);
        state.OwnWallPlanarBodies[42] = (1,body,null);
        var wall = new Element { Id = 42, CategoryId = -2000011 };
        Assert.True(ElementBuilder.ApplyOwnWallPlanarBody(wall,state));
        var writer = new RvtIO.Ifc.StepWriter();
        int product = writer.Add("IFCWALL", "'g',#1,'n',$,$,#2,#3,'42'");
        Assert.Equal(1,new RvtIO.Ifc.IfcExporter().ApplyOwnWallPlanarBodies(writer,
            new Dictionary<int,Element> { [42] = wall },new Dictionary<int,int> { [42] = 1 },
            new Dictionary<int,int> { [product] = 42 },1,2,3));
        var points = Enumerable.Range(0,writer.EntityCount).Select(writer.EntityAt)
            .Where(e => e.Type == "IFCCARTESIANPOINT").Select(e =>
                double.Parse(e.Args.Trim('(',')').Split(',')[0],System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(0.000003048,points.Min(),precision:15);
    }
}
