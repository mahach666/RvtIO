using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The current profile looks for the room of a family instance hosted by a wall at a point one foot beyond the wall's side face that the instance's local Y leaves through, because the box centre of such an instance is inside the wall, in no room.</summary>
public class IfcExporterCurrentWallHostedRoomTests
{
    private const int RoomPhase = 5;
    private const int Wall = 400;

    private static GeometryData Box(double x0, double x1, double y0, double y1, double z0, double z1)
    {
        double[][] p =
        [
            [x0, y0, z0], [x1, y0, z0], [x1, y1, z0], [x0, y1, z0],
            [x0, y0, z1], [x1, y0, z1], [x1, y1, z1], [x0, y1, z1],
        ];
        int[][] faces = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]];
        return new GeometryData
        {
            BoundingBox = [x0, y0, z0, x1, y1, z1],
            Faces = faces.Select(indices => new GeoFace { OuterVertices = indices.Select(index => p[index]).ToList() }).ToList(),
            BrepSolids = [new GeoBrepSolid { IsClosed = true, FaceIndices = [0, 1, 2, 3, 4, 5] }],
        };
    }

    /// <summary>The room is 4 x 3 x 2 ft from the origin; the wall (y from 3 to 3.5) is on its north side.</summary>
    private static Element Room() => new()
    {
        Id = 300, CategoryId = -2000160, Category = "OST_Rooms", Name = "Room",
        Parameters =
        [
            new Parameter { Id = -1006916, StorageType = StorageType.ElementId, Value = 10 },
            new Parameter { Id = -1006901, StorageType = StorageType.String, Value = "1" },
        ],
        SerializedSpaceBoundary = Box(0, 4, 0, 3, 0, 2),
        SerializedRoomPhaseId = RoomPhase,
    };

    private static Element WallElement(int categoryId = -2000011) => new()
    {
        Id = Wall, CategoryId = categoryId, Category = "OST_Walls", Name = "Wall",
        BoundingBox = [0, 3, 0, 4, 3.5, 2], Geometry = Box(0, 4, 3, 3.5, 0, 2),
    };

    /// <summary>A generic model hosted by the wall, its origin in the wall's thickness, its box centre inside the wall.</summary>
    private static Element Instance(double[] basisY, int hostId = Wall)
    {
        var instance = Product(100, -2000151, "OST_GenericModel", 200);
        instance.SerializedOwnElementClassId = 1794;
        instance.SerializedCreatedPhaseId = RoomPhase;
        instance.SerializedRoomId = -1;
        instance.SerializedHostId = hostId;
        instance.BoundingBox = [1.5, 3.1, 0.5, 2.5, 3.4, 1.5];
        instance.Geometry!.BoundingBox = (double[])instance.BoundingBox.Clone();
        instance.Transform = new InstanceTransform
        {
            Origin = [2, 3.25, 1], BasisX = [1, 0, 0], BasisY = basisY, BasisZ = [0, 0, 1],
        };
        return instance;
    }

    private static readonly double[] TowardTheRoom = [0, -1, 0];
    private static readonly double[] AwayFromTheRoom = [0, 1, 0];

    [Fact]
    public void WallSideTestPoint_IsOneFootBeyondTheFaceYLeavesThrough()
    {
        var elements = new Dictionary<int, Element> { [Wall] = WallElement() };

        var toward = IfcExporter.WallSideTestPointCurrent(Instance(TowardTheRoom), elements);
        var away = IfcExporter.WallSideTestPointCurrent(Instance(AwayFromTheRoom), elements);

        Assert.NotNull(toward);
        Assert.Equal([2.0, 2.0, 1.0], toward!.Select(v => Math.Round(v, 9)).ToArray());   // the face y = 3, then one foot into the room
        Assert.NotNull(away);
        Assert.Equal([2.0, 4.5, 1.0], away!.Select(v => Math.Round(v, 9)).ToArray());     // the face y = 3.5, then one foot out
    }

    [Fact]
    public void WallSideTestPoint_NeedsAWallHostWithSideFacesAndATransform()
    {
        var elements = new Dictionary<int, Element> { [Wall] = WallElement() };

        Assert.Null(IfcExporter.WallSideTestPointCurrent(Instance(TowardTheRoom, hostId: 999), elements));          // host unknown
        elements[Wall] = WallElement(categoryId: -2000151);
        Assert.Null(IfcExporter.WallSideTestPointCurrent(Instance(TowardTheRoom), elements));                       // host is no wall
        elements[Wall] = WallElement();
        elements[Wall].Geometry = null;
        Assert.Null(IfcExporter.WallSideTestPointCurrent(Instance(TowardTheRoom), elements));                       // no faces
        elements[Wall] = WallElement();
        var noTransform = Instance(TowardTheRoom);
        noTransform.Transform = null;
        Assert.Null(IfcExporter.WallSideTestPointCurrent(noTransform, elements));
        var sideways = Instance([1, 0, 0]);   // Y along the wall: no side face faces it... the end faces do
        Assert.NotNull(IfcExporter.WallSideTestPointCurrent(sideways, elements));
    }

    [Theory]
    [InlineData(IfcProfile.Current, true, "IFCSPACE")]             // Y leaves the wall into the room
    [InlineData(IfcProfile.Current, false, "IFCBUILDINGSTOREY")]   // Y leaves the wall away from the room
    [InlineData(IfcProfile.Legacy, true, "IFCBUILDINGSTOREY")]    // the legacy profile reads only the stored room
    public void WallHostedInstance_IsInTheRoomBesideTheFaceItsYLeavesThrough(IfcProfile version, bool towardTheRoom, string container)
    {
        var instance = Instance(towardTheRoom ? TowardTheRoom : AwayFromTheRoom);
        string ifc = Export(version, Levels().Concat([Room(), WallElement(), instance]).ToArray());
        Assert.Equal(container, ContainerOf(ifc, "IFCBUILDINGELEMENTPROXY", 100));
    }

    [Fact]
    public void InstanceHostedByAnotherKindOfElement_KeepsTheBoxCentre()
    {
        // Same geometry, but the host is a generic model: the box centre (inside the wall, outside the room) decides.
        var host = WallElement(categoryId: -2000151);
        string ifc = Export(IfcProfile.Current, Levels().Concat([Room(), host, Instance(TowardTheRoom)]).ToArray());
        Assert.Equal("IFCBUILDINGSTOREY", ContainerOf(ifc, "IFCBUILDINGELEMENTPROXY", 100));
    }

    private static IEnumerable<Element> Levels() => [Level(10, "Level A", 0.0), Level(30, "Level C", 10.0)];

    private static Element Level(int id, string name, double elevationFeet) => new()
    {
        Id = id, Name = name, CategoryId = -2000240, Category = "OST_Levels", IsNativeLevel = true,
        SerializedFamilyId = -1, SerializedUnplacedOwnerId = -1, SerializedOwnerDbViewId = -1,
        Parameters =
        [
            new Parameter { Id = -1007102, Definition = new Definition { Name = "LEVEL_ELEV" }, StorageType = StorageType.Double, Value = elevationFeet },
            new Parameter { Id = -1007111, StorageType = StorageType.Integer, Value = 1 },
        ],
    };

    private static Element Product(int id, int categoryId, string category, int typeId) => new()
    {
        Id = id, TypeId = typeId, IsDiscovered = true, CategoryId = categoryId, Category = category, Name = $"Product {id}",
        References = [3],
        Parameters =
        [
            new Parameter { Id = -1012100, StorageType = StorageType.ElementId, Value = 3 },
            new Parameter
            {
                Id = -1002050, Definition = new Definition { Id = -1002050, Name = "ELEM_TYPE_PARAM" },
                StorageType = StorageType.ElementId, Value = typeId,
            },
        ],
        BoundingBox = [0, 0, 10, 4, 3, 12],
        Geometry = Box(0, 4, 0, 3, 10, 12),
    };

    private static string Export(IfcProfile version, params Element[] elements)
    {
        var info = new RevitFileInfo();
        info.Elements.AddRange(elements);
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-currentProfile-wallroom-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter { Profile = version }.Export(info, path);
            return File.ReadAllText(path);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static string? ContainerOf(string ifc, string ifcType, int tag)
    {
        var product = Regex.Match(ifc, $@"#(\d+)={ifcType}\([^\r\n]*,'{tag}'[,)]");
        if (!product.Success) return null;
        foreach (Match relation in Regex.Matches(ifc, @"IFCRELCONTAINEDINSPATIALSTRUCTURE\([^\r\n]*,\(([^)]*)\),#(\d+)\);"))
            if (relation.Groups[1].Value.Split(',').Contains($"#{product.Groups[1].Value}"))
                return Regex.Match(ifc, $@"#{relation.Groups[2].Value}=(\w+)\(").Groups[1].Value;
        return null;
    }
}
