using RvtIO.Ifc;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class OwnCutPairBodyTests
{
    [Fact]
    public void SingleNonRectangularRegionKeepsItsAuthoredProfile()
    {
        var (host, cutter) = Inputs(false);
        var source = OwnWallPlanarBodyTests.Cube();
        foreach (var face in source.Faces)
            face.Vertices = face.Vertices!.Select(p => new[] { p[0] + p[2] * 0.5, p[1] * 0.1, p[2] * 2 }).ToList();
        var body = ElementBuilder.BuildGeometryData(source)!;
        body.BoundingBox = [0, 0, 0, 1.5, 0.1, 2];
        foreach (var face in body.Faces) face.FirstSourceLoop = face.OuterVertices;
        cutter.Geometry = cutter.OwnCurrentCutPairBodyCurrent = body;
        Assert.True(IfcExporter.TryBuildCutPairOpening(host, cutter, out var opening));
        var writer = new StepWriter();

        Assert.True(new IfcExporter().TryCreateOwnCutPairOpeningBodyCurrent(writer, cutter, host,
            opening, 1, out _, out var quantities));

        var values = quantities!.ToDictionary(q => q.Name, q => q.Value);
        Assert.Equal(0.03048, values["Depth"], 12);
        Assert.Equal(1.5 * 0.3048, values["Height"], 12);
        Assert.Equal(2 * 0.3048, values["Width"], 12);
        var entities = Enumerable.Range(0, writer.EntityCount).Select(writer.EntityAt).ToList();
        Assert.Single(entities, entity => entity.Type == "IFCARBITRARYCLOSEDPROFILEDEF");
        Assert.DoesNotContain(entities, entity => entity.Type == "IFCRECTANGLEPROFILEDEF");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleRegionUsesTheSourceSolidAndFirstLoopQuantities(bool holeFirst)
    {
        var (host, cutter) = Inputs(holeFirst);
        var body = cutter.OwnCurrentCutPairBodyCurrent!;
        body.Faces.RemoveRange(body.Faces.Count - 6, 6);
        body.BrepSolids![0].FaceIndices = Enumerable.Range(0, body.Faces.Count).ToList();
        body.BoundingBox = [0, 0, 0, 1, 0.1, 2];
        Assert.True(IfcExporter.TryBuildCutPairOpening(host, cutter, out var opening));
        var writer = new StepWriter();

        Assert.True(new IfcExporter().TryCreateOwnCutPairOpeningBodyCurrent(writer, cutter, host,
            opening, 1, out int shape, out var quantities));

        Assert.True(shape > 0);
        var values = quantities!.ToDictionary(q => q.Name, q => q.Value);
        Assert.Equal(0.03048, values["Depth"], 12);
        Assert.Equal((holeFirst ? 0.25 : 1) * 0.3048, values["Height"], 12);
        Assert.Equal((holeFirst ? 0.5 : 2) * 0.3048, values["Width"], 12);
        var entities = Enumerable.Range(0, writer.EntityCount).Select(writer.EntityAt).ToList();
        Assert.Single(entities, entity => entity.Type == "IFCEXTRUDEDAREASOLID");
        Assert.DoesNotContain(entities, entity => entity.Type == "IFCCOMPOSITEPROFILEDEF");
        Assert.Contains(entities, entity => entity.Type == (holeFirst
            ? "IFCARBITRARYPROFILEDEFWITHVOIDS" : "IFCRECTANGLEPROFILEDEF"));
    }

    [Fact]
    public void CompositeWriterRestoresTheAuthoredFirstRegionAfterAreaClassification()
    {
        var (host,cutter) = Inputs(false);
        var body = cutter.OwnCurrentCutPairBodyCurrent!;
        var original = body.Faces.ToArray();
        foreach (int index in new[] { 2,3 })
        {
            original[index].SourceFace = (uint)index;
            original[index+6].SourceFace = (uint)index;
            original[index+6].FirstSourceLoop = original[index].FirstSourceLoop;
        }
        body.Faces = original.Skip(6).Concat(original.Take(6)).ToList();
        Assert.True(IfcExporter.TryBuildCutPairOpening(host,cutter,out var opening));
        var writer = new StepWriter();
        Assert.True(new IfcExporter().TryCreateOwnCutPairOpeningBodyCurrent(writer,cutter,host,opening,1,out _,out var quantities));
        Assert.Equal(0.3048,quantities!.Single(q=>q.Name=="Height").Value,12);
        var entities = Enumerable.Range(0,writer.EntityCount).Select(writer.EntityAt).ToDictionary(e=>e.Id);
        var composite = entities.Values.Single(e=>e.Type=="IFCCOMPOSITEPROFILEDEF");
        var members = IfcExporter.SplitStepArguments(composite.Args)[2];
        int first = int.Parse(members[2..members.IndexOf(',')]);
        var profile = IfcExporter.SplitStepArguments(entities[first].Args);
        var sizes = profile.Skip(3).Select(s=>double.Parse(s,System.Globalization.CultureInfo.InvariantCulture)).Order().ToArray();
        Assert.Equal(new[]{0.3048,0.6096},sizes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void WriterPreservesAllContoursAndMeasuresTheFirstAuthoredLoop(bool holeFirst)
    {
        var (host, cutter) = Inputs(holeFirst);
        Assert.True(IfcExporter.TryBuildCutPairOpening(host, cutter, out var opening));
        var writer = new StepWriter();
        Assert.True(new IfcExporter().TryCreateOwnCutPairOpeningBodyCurrent(writer, cutter, host,
            opening, 1, out int shape, out var quantities));
        Assert.True(shape > 0);
        var values = quantities!.ToDictionary(q => q.Name, q => q.Value);
        Assert.Equal(0.1 * 0.3048, values["Depth"], 12);
        Assert.Equal((holeFirst ? 0.25 : 1) * 0.3048, values["Height"], 12);
        Assert.Equal((holeFirst ? 0.5 : 2) * 0.3048, values["Width"], 12);
        Assert.Contains(Enumerable.Range(0, writer.EntityCount).Select(writer.EntityAt),
            entity => entity.Type == "IFCCOMPOSITEPROFILEDEF");
        if (holeFirst) Assert.Contains(Enumerable.Range(0, writer.EntityCount).Select(writer.EntityAt),
            entity => entity.Type == "IFCARBITRARYPROFILEDEFWITHVOIDS");
    }

    [Theory]
    [InlineData("no-validation")] [InlineData("open")] [InlineData("no-step")]
    [InlineData("duplicate-step")] [InlineData("no-axis")] [InlineData("zero-axis")]
    [InlineData("nonfinite-axis")] [InlineData("sloping-axis")] [InlineData("cross-section")]
    [InlineData("wrong-operand")] [InlineData("no-source-loop")] [InlineData("legacy")]
    public void UnconfirmedOperandsDoNotAppendWriterEntities(string mode)
    {
        var (host, cutter) = Inputs(false);
        Assert.True(IfcExporter.TryBuildCutPairOpening(host, cutter, out var opening));
        switch (mode)
        {
            case "no-validation": cutter.OwnCurrentCutPairBodyCurrent = null; break;
            case "open": cutter.OwnCurrentCutPairBodyCurrent!.BrepSolids![0].IsClosed = false; break;
            case "no-step": host.CutOutSteps = []; break;
            case "duplicate-step": host.CutOutSteps = [.. host.CutOutSteps!, .. host.CutOutSteps!]; break;
            case "no-axis": host.OwnWallAxisLine = null; break;
            case "zero-axis": host.OwnWallAxisLine = [[0,0,0],[0,0,0]]; break;
            case "nonfinite-axis": host.OwnWallAxisLine![0][0] = double.NaN; break;
            case "sloping-axis": host.OwnWallAxisLine![1][2] = 1; break;
            case "cross-section": host.OwnWallCrossSection = 0; break;
            case "wrong-operand": opening = opening with { WidthFt = 999 }; break;
            case "no-source-loop": foreach (var face in cutter.OwnCurrentCutPairBodyCurrent!.Faces) face.FirstSourceLoop = null; break;
        }
        var writer = new StepWriter();
        var exporter = new IfcExporter { Profile = mode == "legacy" ? IfcProfile.Legacy : IfcProfile.Current };
        Assert.False(exporter.TryCreateOwnCutPairOpeningBodyCurrent(writer, cutter, host, opening, 1, out _, out _));
        Assert.Equal(0, writer.EntityCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SlabCutterIsWrittenOnlyAsTheBrepOfASolidThatIsNoPrismAcrossTheWall(bool sheared)
    {
        var (host, cutter) = Inputs(false);
        cutter.CategoryId = -2000032;
        var body = cutter.Geometry!;
        cutter.OwnCurrentCutPairBodyCurrent = null;
        if (sheared)
            foreach (var face in body.Faces)
            {
                foreach (var point in face.OuterVertices!) point[1] += 0.5 * point[2];
                var v = face.OuterVertices!;
                double[] a = [v[1][0] - v[0][0], v[1][1] - v[0][1], v[1][2] - v[0][2]], b = [v[2][0] - v[0][0], v[2][1] - v[0][1], v[2][2] - v[0][2]];
                face.Normal = [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
            }
        Assert.True(IfcExporter.TryBuildCutPairOpening(host, cutter, out var opening));
        var writer = new StepWriter();

        bool written = new IfcExporter().TryCreateOwnCutPairOpeningBodyCurrent(writer, cutter, host, opening, 1,
            out int shape, out var quantities);

        Assert.Equal(sheared, written);
        var types = Enumerable.Range(0, writer.EntityCount).Select(writer.EntityAt).Select(entity => entity.Type).ToList();
        if (sheared)
        {
            Assert.True(shape > 0);
            Assert.Empty(quantities!);
            Assert.Contains("IFCFACETEDBREP", types);
        }
        else Assert.Empty(types);
    }

    [Fact]
    public void CutterThatIsNoPrismAcrossTheWallIsWrittenAsABrepFromItsMinimumCorner()
    {
        var (host, cutter) = Inputs(false);
        var body = cutter.OwnCurrentCutPairBodyCurrent!;
        // shear across the wall: the side faces tilt against the axis of the extrusion, which has no pair of faces left
        foreach (var face in body.Faces)
        {
            foreach (var point in face.OuterVertices!) point[1] += 0.5 * point[2];
            foreach (var point in face.InnerVertices?.SelectMany(loop => loop) ?? []) point[1] += 0.5 * point[2];
            var v = face.OuterVertices!;
            double[] a = [v[1][0] - v[0][0], v[1][1] - v[0][1], v[1][2] - v[0][2]], b = [v[2][0] - v[0][0], v[2][1] - v[0][1], v[2][2] - v[0][2]];
            face.Normal = [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
        }
        Assert.True(IfcExporter.TryBuildCutPairOpening(host, cutter, out var opening));
        var writer = new StepWriter();

        Assert.True(new IfcExporter().TryCreateOwnCutPairOpeningBodyCurrent(writer, cutter, host, opening, 1,
            out int shape, out var quantities, out var frame));

        Assert.True(shape > 0);
        Assert.Empty(quantities!);
        Assert.Equal([0, 0, 0], frame!.Value.OriginFt);
        Assert.Equal((1, 0), (frame.Value.AxisX, frame.Value.AxisY));
        var entities = Enumerable.Range(0, writer.EntityCount).Select(writer.EntityAt).ToList();
        Assert.Contains(entities, entity => entity.Type == "IFCFACETEDBREP");
        Assert.DoesNotContain(entities, entity => entity.Type == "IFCEXTRUDEDAREASOLID");
        // the vertices are measured from the corner: none is below it
        var points = entities.Where(entity => entity.Type == "IFCCARTESIANPOINT")
            .Select(entity => IfcExporter.SplitStepArguments(entity.Args.Trim('(', ')'))
                .Select(value => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray()).ToList();
        Assert.All(points, point => Assert.All(point, value => Assert.True(value >= -1e-9)));
    }

    [Fact]
    public void ClosedShellsOfOneBodyAreOneSolid_AndAnOpenOneIsNone()
    {
        // The body of a wall carries no node ids: its disjoint closed shells are the one Geometry node handed to CreateOpening
        // (three shells, a Brep). Sheared, so that it is no prism across the wall.
        var (host, cutter) = Inputs(false);
        var body = cutter.OwnCurrentCutPairBodyCurrent!;
        foreach (var face in body.Faces)
        {
            foreach (var point in face.OuterVertices!) point[1] += 0.5 * point[2];
            var v = face.OuterVertices!;
            double[] a = [v[1][0] - v[0][0], v[1][1] - v[0][1], v[1][2] - v[0][2]], b = [v[2][0] - v[0][0], v[2][1] - v[0][1], v[2][2] - v[0][2]];
            face.Normal = [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
        }
        body.BrepSolids = [new() { IsClosed = true, FaceIndices = Enumerable.Range(0, 6).ToList() },
            new() { IsClosed = true, FaceIndices = Enumerable.Range(6, 6).ToList() }];
        Assert.True(IfcExporter.TryBuildCutPairOpening(host, cutter, out var opening));
        var writer = new StepWriter();

        Assert.True(new IfcExporter().TryCreateOwnCutPairOpeningBodyCurrent(writer, cutter, host, opening, 1, out _, out _));
        Assert.Contains(Enumerable.Range(0, writer.EntityCount).Select(writer.EntityAt), entity => entity.Type == "IFCFACETEDBREP");

        body.BrepSolids[1].IsClosed = false;
        Assert.False(new IfcExporter().TryCreateOwnCutPairOpeningBodyCurrent(new StepWriter(), cutter, host, opening, 1, out _, out _));
    }

    private static (Element Host, Element Cutter) Inputs(bool hole)
    {
        var first = Box(1, 2, 0);
        if (hole)
        {
            double[][] ring = [[0.25,0,0.5],[0.5,0,0.5],[0.5,0,1],[0.25,0,1]];
            foreach (int index in new[] { 2, 3 })
            {
                double y = index == 2 ? 0 : 0.1;
                var loop = ring.Select(p => new[] { p[0], y, p[2] }).ToList();
                first[index].InnerVertices = [loop]; first[index].FirstSourceLoop = loop;
            }
            for (int i = 0; i < 4; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % 4];
                first.Add(new() { OuterVertices = [a,b,[b[0],0.1,b[2]],[a[0],0.1,a[2]]] });
            }
        }
        var faces = first.Concat(Box(4, 3, 3)).ToList();
        var body = new GeometryData { Faces = faces, BoundingBox = [0,0,0,7,0.1,3],
            BrepSolids = [new() { IsClosed = true, FaceIndices = Enumerable.Range(0, faces.Count).ToList() }] };
        var cutter = new Element { Id = 42, CategoryId = -2000011, Name = "Wall type",
            Geometry = body, OwnCurrentCutPairBodyCurrent = body };
        var host = new Element { Id = 43, OwnWallCrossSection = 1,
            OwnWallAxisLine = [[0,0,0],[10,0,0]],
            Location = new() { CurveType = "Line", StartPoint = [0,0,0], EndPoint = [10,0,0] },
            CutOutSteps = [(977, cutter.Id, false)] };
        return (host, cutter);
    }

    private static List<GeoFace> Box(double width, double height, double x)
    {
        var source = OwnWallPlanarBodyTests.Cube();
        foreach (var face in source.Faces)
            face.Vertices = face.Vertices!.Select(p => new[] { x + p[0] * width, p[1] * 0.1, p[2] * height }).ToList();
        var geometry = ElementBuilder.BuildGeometryData(source)!;
        foreach (var face in geometry.Faces) face.FirstSourceLoop = face.OuterVertices;
        return geometry.Faces;
    }
}
