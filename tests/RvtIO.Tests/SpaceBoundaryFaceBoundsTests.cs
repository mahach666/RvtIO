using RvtIO.Ifc;
using RvtIO.Model;
using RvtIO.Parsing;
using System.Text.RegularExpressions;
using Xunit;

namespace RvtIO.Tests;

/// <summary>
/// The current profile writes the body of a space with one IfcFace per serialized Face and a
/// bound for every loop of the Face. A Face of a room boundary that owns disjoint coplanar regions (the ceiling of a room with a
/// stepped ceiling height) is therefore one IfcFace whose loops are all bounds, the last of the loops that lie inside no earlier one being
/// the IfcFaceOuterBound. sample project A room 6957647: 17 faces, one of them [FaceBound, FaceBound, FaceOuterBound].
/// </summary>
public class SpaceBoundaryFaceBoundsTests
{
    private static string ExportRoom(string base64, uint owner, out GeometryData geometry)
    {
        byte[] data = Convert.FromBase64String(base64);
        var body = GRepParserV2.ParseExactPlanarSpaceBoundary(data, 0, (uint)(data.Length - 16), owner)!;
        geometry = ElementBuilder.BuildGeometryData(body)!;
        var level = new Element
        {
            Id = 900, Name = "Level", IsNativeLevel = true, SerializedFamilyId = -1, SerializedUnplacedOwnerId = -1,
            SerializedOwnerDbViewId = -1,
            Parameters =
            [
                new() { Id = -1007102, Definition = new Definition { Name = "LEVEL_ELEV" }, StorageType = StorageType.Double, Value = 0.0 },
                new() { Id = -1007111, StorageType = StorageType.Integer, Value = 1 },
            ],
        };
        var room = new Element
        {
            Id = (int)owner, CategoryId = -2000160, Category = "OST_Rooms", Name = "Room",
            Parameters =
            [
                new() { Id = -1006916, StorageType = StorageType.ElementId, Value = 900 },
                new() { Id = -1006901, StorageType = StorageType.String, Value = "1" },
            ],
            SerializedSpaceBoundary = geometry,
        };
        var info = new RevitFileInfo();
        info.Elements.Add(level);
        info.Elements.Add(room);
        string path = Path.Combine(Path.GetTempPath(), $"space-bounds-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter { Profile = IfcProfile.Current }.Export(info, path);
            return File.ReadAllText(path);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static List<string[]> FaceBoundTypes(string ifc)
    {
        var types = Regex.Matches(ifc, @"#(\d+)=(IFCFACEOUTERBOUND|IFCFACEBOUND)\(")
            .ToDictionary(m => int.Parse(m.Groups[1].Value), m => m.Groups[2].Value);
        return Regex.Matches(ifc, @"#\d+=IFCFACE\(\(([^)]*)\)\);")
            .Select(m => Regex.Matches(m.Groups[1].Value, @"#(\d+)").Select(r => types[int.Parse(r.Groups[1].Value)]).ToArray())
            .ToList();
    }

    [Fact]
    public void DisjointCoplanarLoopsOfOneFace_AreTheBoundsOfOneIfcFace()
    {
        string ifc = ExportRoom(GRepParserV2SpaceBoundaryWeldTests.Room6957647, 6957647, out var geometry);
        // 19 polygons come from the 17 serialized Faces of the cache.
        Assert.Equal(19, geometry.Faces.Count);
        Assert.Equal(17, geometry.Faces.Select(face => face.SourceFace).Distinct().Count());
        Assert.All(geometry.Faces, face => Assert.NotNull(face.SourceLoops));

        var faces = FaceBoundTypes(ifc);
        Assert.Equal(17, faces.Count);
        var bounded = Assert.Single(faces, bounds => bounds.Length > 1);
        Assert.Equal(["IFCFACEBOUND", "IFCFACEBOUND", "IFCFACEOUTERBOUND"], bounded);
        Assert.All(faces.Where(bounds => bounds.Length == 1), bounds => Assert.Equal("IFCFACEOUTERBOUND", bounds[0]));
    }
}
