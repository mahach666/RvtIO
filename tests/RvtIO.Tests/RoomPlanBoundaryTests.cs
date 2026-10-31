using System.Globalization;
using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;
using Key = RvtIO.Parsing.TfBlockDeserializer.SegmentKey;
using Segment = RvtIO.Parsing.TfBlockDeserializer.PlanSegment;

namespace RvtIO.Tests;

/// <summary>
/// A room or area with no geometry block is extruded by the current profile from the circuit of its level room plan (sample project C and sample school project: the 51
/// areas, vertex for vertex; sample project A area 2155968). A side of the circuit is a segment of the plan, walked backwards when its sub index is
/// negative.
/// </summary>
public class RoomPlanBoundaryTests
{
    private static Segment Line(int item, int sub, (double X, double Y) a, (double X, double Y) b) =>
        new(new Key(item, -1, 0, sub), 0, 1, 0, 0, [a, b]);

    private static TfBlockDeserializer.RoomPlanTopology Plan(Segment[] segments, Key[] sides, (int Outer, int Enclosing)[]? components = null) =>
        new([1], new Dictionary<int, double> { [5] = 1 }, components ?? [], segments, new Dictionary<int, IReadOnlyList<Key>> { [5] = sides });

    private static Key Side(Segment segment, bool backwards = false) =>
        backwards ? segment.Key with { Sub = -segment.Key.Sub - 1 } : segment.Key;

    private static readonly Segment South = Line(10, 0, (0, 0), (4, 0)), East = Line(11, 0, (4, 0), (4, 3)),
        North = Line(12, 0, (4, 3), (0, 3)), West = Line(13, 0, (0, 3), (0, 0));

    [Fact]
    public void ASquareLoop_IsItsFourCorners()
    {
        var loop = RoomPlanBoundary.OuterLoop(Plan([South, East, North, West], [Side(South), Side(East), Side(North), Side(West)]), 5);
        Assert.Equal([(0.0, 0.0), (4.0, 0.0), (4.0, 3.0), (0.0, 3.0)], loop);
    }

    [Fact]
    public void ASideWithANegativeSubIndex_IsTheSegmentWalkedBackwards()
    {
        var backwardsSouth = Line(10, 0, (4, 0), (0, 0));
        var loop = RoomPlanBoundary.OuterLoop(Plan([backwardsSouth, East, North, West],
            [Side(backwardsSouth, backwards: true), Side(East), Side(North), Side(West)]), 5);
        Assert.Equal([(0.0, 0.0), (4.0, 0.0), (4.0, 3.0), (0.0, 3.0)], loop);
    }

    [Fact]
    public void TwoCrossingLinesWithAGap_AreCutAtTheirCrossing()
    {
        // the vertical line starts 0.5 beyond the corner: its start moves to the corner
        var east = Line(11, 0, (4, 0.5), (4, 3));
        var loop = RoomPlanBoundary.OuterLoop(Plan([South, east, North, West], [Side(South), Side(east), Side(North), Side(West)]), 5);
        Assert.Equal([(0.0, 0.0), (4.0, 0.0), (4.0, 3.0), (0.0, 3.0)], loop);
    }

    [Fact]
    public void TwoParallelLinesWithAGap_AreJoinedByAStraightLine()
    {
        var south = Line(10, 0, (0, 0), (4, 0));
        var jog = Line(14, 0, (4, 1), (9, 1));
        var east = Line(11, 0, (9, 1), (9, 3));
        var north = Line(12, 0, (9, 3), (0, 3));
        var loop = RoomPlanBoundary.OuterLoop(Plan([south, jog, east, north, West], [Side(south), Side(jog), Side(east), Side(north), Side(West)]), 5);
        Assert.Equal([(0.0, 0.0), (4.0, 0.0), (4.0, 1.0), (9.0, 1.0), (9.0, 3.0), (0.0, 3.0)], loop);
    }

    [Fact]
    public void AGapThatClosesTheLoopBetweenParallelLines_StartsTheLoopAtTheLastEnd()
    {
        // the last curve (y = 1) ends 1 above the start of the first (y = 0): a straight line joins them and comes first in the loop
        var a = Line(10, 0, (0, 0), (4, 0));
        var b = Line(11, 0, (4, 0), (4, 3));
        var c = Line(12, 0, (4, 3), (1, 3));
        var d = Line(13, 0, (1, 3), (1, 1));
        var e = Line(14, 0, (1, 1), (0, 1));
        var loop = RoomPlanBoundary.OuterLoop(Plan([a, b, c, d, e], [Side(a), Side(b), Side(c), Side(d), Side(e)]), 5);
        Assert.Equal([(0.0, 1.0), (0.0, 0.0), (4.0, 0.0), (4.0, 3.0), (1.0, 3.0), (1.0, 1.0)], loop);
    }

    [Fact]
    public void ASegmentWalkedOutAndBack_IsNoPartOfTheLoop()
    {
        var spur = Line(15, 0, (4, 3), (6, 3));
        var loop = RoomPlanBoundary.OuterLoop(Plan([South, East, spur, North, West],
            [Side(South), Side(East), Side(spur), Side(spur, backwards: true), Side(North), Side(West)]), 5);
        Assert.Equal([(0.0, 0.0), (4.0, 0.0), (4.0, 3.0), (0.0, 3.0)], loop);
    }

    [Fact]
    public void AnArc_IsSampledInStepsOfAtMostPiOver16()
    {
        // a quarter circle of radius 10 about (0, 0) from (10, 0) to (0, 10), stored as a fine polyline of 26 points
        var points = Enumerable.Range(0, 26).Select(i => (10 * Math.Cos(Math.PI / 2 * i / 25), 10 * Math.Sin(Math.PI / 2 * i / 25))).ToList();
        var arc = new Segment(new Key(20, -1, 0, 0), 0, 1, 0, 0, points);
        var close = Line(21, 0, (0, 10), (10, 0));
        var loop = RoomPlanBoundary.OuterLoop(Plan([arc, close], [Side(arc), Side(close)]), 5)!;
        // 8 steps of pi/16 cover the quarter circle exactly: 9 sample points, the last of the arc is the start of the line
        Assert.Equal(9 + 1 - 1, loop.Count);
        Assert.Equal((10.0, 0.0), loop[0]);
        Assert.All(loop, point => Assert.Equal(10, Math.Sqrt(point.X * point.X + point.Y * point.Y), 9));
    }

    [Fact]
    public void APolylineWindingRoundTheCircleMoreThanOnce_IsNoArc()
    {
        // 200 points in equal steps of 3 rad on a circle: a sweep of about 600 rad. It is no arc of the plan, and the number of samples
        // must not follow the stored points (3,000 samples without the bound).
        var points = Enumerable.Range(0, 200).Select(i => (10 * Math.Cos(3.0 * i), 10 * Math.Sin(3.0 * i))).ToList();
        var winding = new Segment(new Key(20, -1, 0, 0), 0, 1, 0, 0, points);
        var close = Line(21, 0, points[^1], points[0]);
        Assert.Null(RoomPlanBoundary.OuterLoop(Plan([winding, close], [Side(winding), Side(close)]), 5, out string? failure));
        Assert.Contains("more than a full turn", failure);
    }

    [Fact]
    public void AnArcOf350Degrees_IsStillSampledIn32Steps()
    {
        // the longest arc the bound lets through is a full turn; 350 degrees in 70 equal steps are 32 steps of at most pi/16
        var points = Enumerable.Range(0, 71).Select(i => (10 * Math.Cos(Math.PI / 36 * i), 10 * Math.Sin(Math.PI / 36 * i))).ToList();
        var arc = new Segment(new Key(20, -1, 0, 0), 0, 1, 0, 0, points);
        var close = Line(21, 0, points[^1], points[0]);
        var loop = RoomPlanBoundary.OuterLoop(Plan([arc, close], [Side(arc), Side(close)]), 5);
        Assert.NotNull(loop);
        Assert.Equal(32 + 1, loop!.Count);
    }

    [Fact]
    public void AnArcOfThreePoints_ShorterThanOneStep_IsItsChord()
    {
        // a 6 degree arc of radius 10 stored as three points: one step of pi/16 covers it, the expected profile has the two ends only
        var points = Enumerable.Range(0, 3).Select(i => (10 * Math.Cos(Math.PI / 30 * i / 2), 10 * Math.Sin(Math.PI / 30 * i / 2))).ToList();
        var arc = new Segment(new Key(20, -1, 0, 0), 0, 1, 0, 0, points);
        var close = Line(21, 0, points[2], (0, 5));
        var back = Line(22, 0, (0, 5), points[0]);
        var loop = RoomPlanBoundary.OuterLoop(Plan([arc, close, back], [Side(arc), Side(close), Side(back)]), 5)!;
        Assert.Equal([points[0], points[2], (0.0, 5.0)], loop);
    }

    [Fact]
    public void ASegmentOfPointsInOrderOnOneLine_IsTheLineBetweenItsEnds()
    {
        // sample project A 2155968: a side of three points on a diagonal; the expected profile has no vertex at the middle one
        var south = new Segment(new Key(10, -1, 0, 0), 0, 1, 0, 0, [(0, 0), (1, 0), (4, 0)]);
        var loop = RoomPlanBoundary.OuterLoop(Plan([south, East, North, West], [Side(south), Side(East), Side(North), Side(West)]), 5);
        Assert.Equal([(0.0, 0.0), (4.0, 0.0), (4.0, 3.0), (0.0, 3.0)], loop);
        // walked backwards the same
        var north = new Segment(new Key(12, -1, 0, 0), 0, 1, 0, 0, [(0, 3), (3, 3), (4, 3)]);
        var backwards = RoomPlanBoundary.OuterLoop(Plan([South, East, north, West], [Side(South), Side(East), Side(north, backwards: true), Side(West)]), 5);
        Assert.Equal([(0.0, 0.0), (4.0, 0.0), (4.0, 3.0), (0.0, 3.0)], backwards);
        // a segment that goes out along the line and comes back is none
        var folded = new Segment(new Key(10, -1, 0, 0), 0, 1, 0, 0, [(0, 0), (3, 0), (1, 0), (4, 0)]);
        Assert.Null(RoomPlanBoundary.OuterLoop(Plan([folded, East, North, West], [Side(folded), Side(East), Side(North), Side(West)]), 5));
    }

    [Fact]
    public void AnythingThatCannotBeTold_HasNoLoop()
    {
        var sides = new[] { Side(South), Side(East), Side(North), Side(West) };
        // a side without a segment
        Assert.Null(RoomPlanBoundary.OuterLoop(Plan([South, East, North], sides), 5));
        // an island in the circuit
        Assert.Null(RoomPlanBoundary.OuterLoop(Plan([South, East, North, West], sides, [(6, 5)]), 5));
        // a polyline that is not an arc
        var bent = new Segment(new Key(10, -1, 0, 0), 0, 1, 0, 0, [(0, 0), (1, 1), (2, 1.2), (3, 1), (4, 0)]);
        Assert.Null(RoomPlanBoundary.OuterLoop(Plan([bent, East, North, West], [Side(bent), Side(East), Side(North), Side(West)]), 5));
        // a gap along one line
        var farEast = Line(11, 0, (4, 1), (4, 3));
        var southToCorner = Line(10, 0, (0, 0), (4, 0));
        var along = Line(16, 0, (4, 0), (4, 0.5));
        var apart = Line(17, 0, (4, 0.7), (4, 1));
        Assert.Null(RoomPlanBoundary.OuterLoop(Plan([southToCorner, along, apart, farEast, North, West],
            [Side(southToCorner), Side(along), Side(apart), Side(farEast), Side(North), Side(West)]), 5));
        // no such circuit
        Assert.Null(RoomPlanBoundary.OuterLoop(Plan([South], [Side(South)]), 99));
    }

    [Fact]
    public void TheBuilder_GivesAnElementWithoutAGeometryBlockItsOutline()
    {
        var plan = Plan([South, East, North, West], [Side(South), Side(East), Side(North), Side(West)]);
        var state = new ParsingState();
        state.RoomPlanSource[200] = (1, plan);
        foreach (uint id in new uint[] { 1, 2, 3 })
        {
            state.RoomNameSource[id] = (1, "Room");
            state.ExactRoomCircuitIds[id] = 5;
        }
        state.SpaceBoundarySource[2] = (1, null);                         // a geometry block, whatever came of it
        var area = new Element { Id = 1, CategoryId = -2003200 };
        var withBlock = new Element { Id = 2, CategoryId = -2000160 };
        var other = new Element { Id = 3, CategoryId = -2000011 };
        var info = new RevitFileInfo { Elements = [area, withBlock, other] };
        ElementBuilder.ApplySerializedPlanBoundaries(info, state);
        Assert.Equal(4, area.SerializedPlanBoundary!.Count);
        Assert.Null(withBlock.SerializedPlanBoundary);
        Assert.Null(other.SerializedPlanBoundary);
    }

    [Fact]
    public void AnArea_GetsThePlanAreaOfItsCircuitLikeARoom()
    {
        var plan = Plan([South, East, North, West], [Side(South), Side(East), Side(North), Side(West)]);
        var state = new ParsingState();
        state.RoomPlanSource[200] = (1, plan);
        state.RoomNameSource[1] = (1, "Area");
        state.ExactRoomCircuitIds[1] = 5;
        var area = new Element { Id = 1, CategoryId = -2003200 };
        ElementBuilder.ApplySerializedRoomPlanAreas(new RevitFileInfo { Elements = [area] }, state);
        Assert.Equal(1, area.SerializedRoomPlanAreaSquareFeet);        // the area of circuit 5 in the plan under test
    }

    private static string Export(Element room, IfcProfile version)
    {
        Element Level(int id, double elevation) => new()
        {
            Id = id, Name = "Level" + id, IsNativeLevel = true, SerializedFamilyId = -1, SerializedUnplacedOwnerId = -1, SerializedOwnerDbViewId = -1,
            Parameters =
            [
                new() { Id = -1007102, Definition = new Definition { Name = "LEVEL_ELEV" }, StorageType = StorageType.Double, Value = elevation },
                new() { Id = -1007111, StorageType = StorageType.Integer, Value = 1 },
            ],
        };
        var info = new RevitFileInfo();
        info.Elements.Add(Level(900, 0));
        info.Elements.Add(Level(901, 12));
        info.Elements.Add(room);
        string path = Path.Combine(Path.GetTempPath(), $"plan-space-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter { Profile = version }.Export(info, path);
            return File.ReadAllText(path);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static Element Area(IReadOnlyList<(double X, double Y)>? outline) => new()
    {
        Id = 77, CategoryId = -2003200, Category = "OST_Areas", Name = "Area",
        Parameters =
        [
            new() { Id = -1006916, StorageType = StorageType.ElementId, Value = 900 },
            new() { Id = -1006901, StorageType = StorageType.String, Value = "1" },
        ],
        SerializedPlanBoundary = outline,
    };

    [Fact]
    public void AnAreaWithAnOutline_IsExtrudedByTheHeightOfItsLevel()
    {
        string ifc = Export(Area([(0, 0), (4, 0), (4, 3), (0, 3), (0, 1.5)]), IfcProfile.Current);
        var solid = Regex.Match(ifc, "=IFCEXTRUDEDAREASOLID[(]#[0-9]+,#[0-9]+,#[0-9]+,([^)]*)[)];");
        Assert.True(solid.Success);
        Assert.Equal(12 * 304.8, double.Parse(solid.Groups[1].Value.TrimEnd('.'), CultureInfo.InvariantCulture), 6);
        Assert.Contains("=IFCARBITRARYCLOSEDPROFILEDEF(", ifc);
        // five vertices and the closing point
        var polyline = Regex.Match(ifc, "=IFCPOLYLINE[(][(]([^)]*)[)][)];");
        Assert.True(polyline.Success);
        Assert.Equal(6, polyline.Groups[1].Value.Split(',').Length);
    }

    [Fact]
    public void ARectangularOutline_IsARectangleProfile()
    {
        string ifc = Export(Area([(0, 0), (4, 0), (4, 3), (0, 3)]), IfcProfile.Current);
        Assert.Contains("=IFCRECTANGLEPROFILEDEF(", ifc);
    }

    [Fact]
    public void WithoutAnOutline_OrForLegacy_NothingIsExtruded()
    {
        Assert.DoesNotContain("=IFCEXTRUDEDAREASOLID(", Export(Area(null), IfcProfile.Current));
        Assert.DoesNotContain("=IFCEXTRUDEDAREASOLID(", Export(Area([(0, 0), (4, 0), (4, 3), (0, 3), (0, 1.5)]), IfcProfile.Legacy));
    }
}
