using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>
/// The planar body of a room is written as the polygons of its coedges, one IfcFace per serialized Face, without welding the vertices or confirming
/// that the shell closes. sample project A room 5992163 has a sliver wall 0.9 mm wide next to a gap of
/// 0.36 mm between two Edges (the welded reader cannot close it: 21 faces expected, no body in ours) and room 2766296 an Edge shared by four Faces
/// (the shell cannot be oriented: 28 faces expected).
/// </summary>
public class SpaceBoundaryLiteralTests
{
    private static byte[] Block(string name)
    {
        using var stream = TestFiles.OpenBlock($"{name}.bin");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static GRepData? Literal(string name, uint owner)
    {
        byte[] data = Block(name);
        return GRepParserV2.ParseLiteralPlanarSpaceBoundary(data, 0, (uint)(data.Length - 16), owner);
    }

    private static string Export(GeometryData literal, int roomId, IfcProfile version)
    {
        var level = new Element
        {
            Id = 900, Name = "Level", IsNativeLevel = true, SerializedFamilyId = -1, SerializedUnplacedOwnerId = -1, SerializedOwnerDbViewId = -1,
            Parameters =
            [
                new() { Id = -1007102, Definition = new Definition { Name = "LEVEL_ELEV" }, StorageType = StorageType.Double, Value = 0.0 },
                new() { Id = -1007111, StorageType = StorageType.Integer, Value = 1 },
            ],
        };
        var room = new Element
        {
            Id = roomId, CategoryId = -2000160, Category = "OST_Rooms", Name = "Room",
            Parameters =
            [
                new() { Id = -1006916, StorageType = StorageType.ElementId, Value = 900 },
                new() { Id = -1006901, StorageType = StorageType.String, Value = "1" },
            ],
            SerializedLiteralSpaceBoundary = literal,
        };
        var info = new RevitFileInfo();
        info.Elements.Add(level);
        info.Elements.Add(room);
        string path = Path.Combine(Path.GetTempPath(), $"space-literal-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter { Profile = version }.Export(info, path);
            return File.ReadAllText(path);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [SkippableFact]
    public void TheSliverRoom_IsRefusedByTheWeldedReader_AndReadLiterally()
    {
        byte[] data = Block("sample-a-room-5992163-sliver");
        uint size = (uint)(data.Length - 16);
        Assert.Null(GRepParserV2.ParseExactPlanarSpaceBoundary(data, 0, size, 5992163));
        var literal = Assert.IsType<GRepData>(Literal("sample-a-room-5992163-sliver", 5992163));
        Assert.Equal(21, literal.Faces.Count);
        // the ceiling has two loops: a 10-gon and, beside it, a triangle that lies inside no other loop and so is the outer bound
        var ceiling = Assert.Single(literal.Faces, face => face.SourceLoops!.Count == 2);
        Assert.Equal([10, 3], ceiling.SourceLoops!.Select(loop => loop.Count));
        Assert.Equal(3, ceiling.Vertices!.Count);
    }

    [SkippableFact]
    public void TheSliverRoom_IsWrittenAsItsTwentyOneFaces_WithTheGapBetweenTheEdgesKept()
    {
        var geometry = ElementBuilder.BuildGeometryData(Literal("sample-a-room-5992163-sliver", 5992163)!)!;
        string ifc = Export(geometry, 5992163, IfcProfile.Current);
        Assert.Single(Regex.Matches(ifc, "#[0-9]+=IFCFACETEDBREP[(]"));
        Assert.Equal(21, Regex.Matches(ifc, "#[0-9]+=IFCFACE[(]").Count);
        Assert.Single(Regex.Matches(ifc, "#[0-9]+=IFCFACEBOUND[(]"));
        // the two ends of the gap are two points (0.36 mm apart), not one
        var points = Regex.Matches(ifc, @"#[0-9]+=IFCCARTESIANPOINT[(][(]([-0-9.E]+),([-0-9.E]+),[-0-9.E]+[)][)];")
            .Select(m => (X: double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                Y: double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture))).ToList();
        Assert.Contains(points, p => Math.Abs(p.X - -7744.3595) < 0.001 && Math.Abs(p.Y - 1856.7504) < 0.001);
        Assert.Contains(points, p => Math.Abs(p.X - -7744.1057) < 0.001 && Math.Abs(p.Y - 1857.0042) < 0.001);
    }

    [SkippableFact]
    public void TheRoomWithAnEdgeOfFourFaces_IsWrittenAsItsTwentyEightFaces()
    {
        var literal = Literal("sample-a-room-2766296-fourfaces", 2766296)!;
        Assert.Equal(28, literal.Faces.Count);
        string ifc = Export(ElementBuilder.BuildGeometryData(literal)!, 2766296, IfcProfile.Current);
        Assert.Equal(28, Regex.Matches(ifc, "#[0-9]+=IFCFACE[(]").Count);
    }

    [SkippableFact]
    public void TheLiteralReading_IsNotUsedForLegacy()
    {
        var geometry = ElementBuilder.BuildGeometryData(Literal("sample-a-room-5992163-sliver", 5992163)!)!;
        Assert.DoesNotContain("=IFCFACETEDBREP(", Export(geometry, 5992163, IfcProfile.Legacy));
    }

    [SkippableFact]
    public void ACacheThatIsNotPlanar_HasNoLiteralReading()
    {
        byte[] data = Block("sample-a-room-8964053-curved");
        Assert.Null(GRepParserV2.ParseLiteralPlanarSpaceBoundary(data, 0, (uint)(data.Length - 16), 8964053));
    }
}
