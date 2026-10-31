using System.Reflection;
using RvtIO.Ifc;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The panels of a curtain wall are exported in the order its CurtainGrid stores them (the panel ids of the grid of each driver).
/// The wall block of a curtain wall with 64 panel ids, some of them cells without an exported panel, is the fixture.</summary>
public class CurtainPanelOrderTests
{
    private static byte[] WallBlock()
    {
        using Stream stream = TestFiles.OpenBlock("sample_a_curtain_wall_9169323.bin");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }


    [SkippableFact]
    public void WallBlock_GivesThePanelIdsInStoredOrder()
    {
        byte[] block = WallBlock();
        int[]? ids = TfBlockDeserializer.ExtractCurtainGridPanelIds(block, 0, BitConverter.ToUInt32(block, 8),
            BitConverter.ToUInt16(block, 12));

        Assert.NotNull(ids);
        Assert.Equal(64, ids.Length);
        Assert.Equal([9169324, 9169326, 9169383, 9169364, 9169328, 9169374, 9169393, 9169354], ids.Take(8));
        Assert.Equal(9169478, ids[^1]);
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [SkippableFact]
    public void BlockOfAnotherClass_OrWithAWrongSize_HasNoPanelOrder()
    {
        byte[] block = WallBlock();
        uint size = BitConverter.ToUInt32(block, 8);
        // not a wall
        Assert.Null(TfBlockDeserializer.ExtractCurtainGridPanelIds(block, 0, size, 1794));
        // the footer does not match the size
        Assert.Null(TfBlockDeserializer.ExtractCurtainGridPanelIds(block, 0, size - 4, BitConverter.ToUInt16(block, 12)));
        // a truncated block
        Assert.Null(TfBlockDeserializer.ExtractCurtainGridPanelIds(block[..(block.Length - 100)], 0, size,
            BitConverter.ToUInt16(block, 12)));
    }

    [SkippableFact]
    public void RecordedOrder_IsAmbiguousWhenTheWallHasTwoRoots()
    {
        byte[] block = WallBlock();
        uint size = BitConverter.ToUInt32(block, 8);
        ushort cls = BitConverter.ToUInt16(block, 12);
        var state = new ParsingState();

        RvtFileReader.RecordCurtainPanelOrder(state, block, 0, size, cls, 9169323);
        Assert.Equal(1, state.CurtainPanelOrders[9169323].BlockCount);
        Assert.Equal(64, state.CurtainPanelOrders[9169323].PanelIds!.Length);

        RvtFileReader.RecordCurtainPanelOrder(state, block, 0, size, cls, 9169323);
        Assert.Equal((2, (int[]?)null), state.CurtainPanelOrders[9169323]);

        // the id of the block must be the owner it is recorded for
        RvtFileReader.RecordCurtainPanelOrder(state, block, 0, size, cls, 7);
        Assert.Null(state.CurtainPanelOrders[7].PanelIds);
    }

    [Fact]
    public void AppliedOrder_NeedsAUniqueRootAndAWall()
    {
        var state = new ParsingState();
        state.CurtainPanelOrders[10] = (1, [3, 2, 1]);
        state.CurtainPanelOrders[11] = (2, null);
        state.CurtainPanelOrders[12] = (1, [1]);
        var wall = new Element { Id = 10, CategoryId = -2000011 };
        var duplicated = new Element { Id = 11, CategoryId = -2000011 };
        var notAWall = new Element { Id = 12, CategoryId = -2000151 };
        var unknown = new Element { Id = 13, CategoryId = -2000011, SerializedCurtainPanelOrder = [9] };

        ElementBuilder.ApplyCurtainPanelOrders([wall, duplicated, notAWall, unknown], state);

        Assert.Equal([3, 2, 1], wall.SerializedCurtainPanelOrder);
        Assert.Null(duplicated.SerializedCurtainPanelOrder);
        Assert.Null(notAWall.SerializedCurtainPanelOrder);
        Assert.Null(unknown.SerializedCurtainPanelOrder);
    }


    private static Element Child(int id, int category = -2000170) => new() { Id = id, CategoryId = category };

    [Fact]
    public void StoredOrder_ListsThePanelsOnceAndSkipsWhatIsNotExported_MullionsFollowById()
    {
        var host = new Element { Id = 1, CategoryId = -2000011, SerializedCurtainPanelOrder = [30, 99, 10, 30, 0, 20] };
        var children = new[] { Child(10), Child(20), Child(30), Child(70, -2000171), Child(60, -2000171) };

        Assert.Equal([30, 10, 20, 60, 70], IfcExporter.CurtainExportOrderCurrent(host, children));
    }

    [Fact]
    public void StoredOrder_LeavesAPanelTheGridDoesNotListToTheEndOfThePanelsById()
    {
        var host = new Element { Id = 1, CategoryId = -2000011, SerializedCurtainPanelOrder = [30, 10] };
        var children = new[] { Child(50), Child(10), Child(20), Child(30), Child(5, -2000171) };

        Assert.Equal([30, 10, 20, 50, 5], IfcExporter.CurtainExportOrderCurrent(host, children));
    }

    [Fact]
    public void StoredOrder_WinsOverTheCellGeometry()
    {
        var host = new Element
        {
            Id = 1, CategoryId = -2000011, SerializedCurtainPanelOrder = [20, 10],
            Location = new LocationCurve { CurveType = "Line", StartPoint = [0, 0, 0], EndPoint = [10, 0, 0] },
        };
        var children = new[]
        {
            new Element { Id = 10, CategoryId = -2000170, BoundingBox = [0, 0, 0, 3, 0.2, 2] },
            new Element { Id = 20, CategoryId = -2000170, BoundingBox = [3, 0, 0, 6, 0.2, 2] },
        };

        Assert.Equal([20, 10], IfcExporter.CurtainExportOrderCurrent(host, children));
        host.SerializedCurtainPanelOrder = null;
        Assert.Equal([10, 20], IfcExporter.CurtainExportOrderCurrent(host, children));
    }

    [Fact]
    public void HostsAreWalkedByElementId_AndHostsWithoutExportedChildrenAreDropped()
    {
        var elements = new Dictionary<int, Element>
        {
            [5] = new() { Id = 5, CategoryId = -2000011, SerializedCurtainPanelOrder = [51, 50] },
            [3] = new() { Id = 3, CategoryId = -2000011, SerializedCurtainPanelOrder = [31, 30] },
            [4] = new() { Id = 4, CategoryId = -2000011 },
            [30] = Child(30), [31] = Child(31), [50] = Child(50), [51] = Child(51), [40] = Child(40),
        };
        var hosts = new Dictionary<int, List<int>> { [5] = [50, 51], [3] = [30, 31], [4] = [40], [9] = [90] };

        var orders = IfcExporter.CurtainExportOrdersCurrent(hosts, elements, id => id != 40);

        Assert.Equal([3, 5], orders.Select(item => item.HostId));
        Assert.Equal([31, 30], orders[0].Order);
        Assert.Equal([51, 50], orders[1].Order);
    }


    private static string Text(StepWriter w)
    {
        using var text = new StringWriter();
        w.WriteTo(text, "t.ifc", "IFC2X3", "t", "t");
        return text.ToString();
    }

    [Fact]
    public void PlateType_IsTaggedByAndListsItsPanelsInExportOrder()
    {
        var w = new StepWriter();
        int first = w.Add("IFCPLATE", "'G1',#1,'P',$,$,$,$,'101',$");   // element 101
        int second = w.Add("IFCPLATE", "'G2',#1,'P',$,$,$,$,'102',$");  // element 102
        int third = w.Add("IFCPLATE", "'G3',#1,'P',$,$,$,$,'103',$");   // element 103
        int type = w.Add("IFCPLATETYPE", "'GT',#1,'T',$,$,$,$,'101',$,.CURTAIN_PANEL.");
        w.Add("IFCRELDEFINESBYTYPE", $"'GR',#1,$,$,(#{first},#{second},#{third}),#{type}");
        var elementByEntity = new Dictionary<int, int> { [first] = 101, [second] = 102, [third] = 103 };
        // the host of 103 is exported before the host of 101 and 102, which are in one wall in the order 102, 101
        var rank = new Dictionary<int, (int HostId, int Position)> { [103] = (1, 4), [101] = (2, 1), [102] = (2, 0) };

        Assert.Equal(1, IfcExporter.ApplyCurtainPanelTypeOrderCurrent(w, elementByEntity, rank));

        string text = Text(w);
        Assert.Contains($"(#{third},#{second},#{first}),#{type}", text);
        Assert.Contains("'GT',#1,'T',$,$,$,$,'103',$,.CURTAIN_PANEL.", text);
        // done once, the second pass has nothing to change
        Assert.Equal(0, IfcExporter.ApplyCurtainPanelTypeOrderCurrent(w, elementByEntity, rank));
    }

    [Fact]
    public void PlateTypeOfPanelsOutsideACurtainWall_KeepsTheLowestElementIdFirst()
    {
        var w = new StepWriter();
        int a = w.Add("IFCPLATE", "'G1',#1,'P',$,$,$,$,'8',$");
        int b = w.Add("IFCPLATE", "'G2',#1,'P',$,$,$,$,'7',$");
        int type = w.Add("IFCPLATETYPE", "'GT',#1,'T',$,$,$,$,'8',$,.NOTDEFINED.");
        w.Add("IFCRELDEFINESBYTYPE", $"'GR',#1,$,$,(#{a},#{b}),#{type}");
        var elementByEntity = new Dictionary<int, int> { [a] = 8, [b] = 7 };

        Assert.Equal(1, IfcExporter.ApplyCurtainPanelTypeOrderCurrent(w, elementByEntity,
            new Dictionary<int, (int, int)> { [99] = (1, 0) }));

        string text = Text(w);
        Assert.Contains($"(#{b},#{a}),#{type}", text);
        Assert.Contains("'T',$,$,$,$,'7',$,.NOTDEFINED.", text);
    }

    [Fact]
    public void OtherTypesAndUnresolvedMembers_AreLeftAlone()
    {
        var w = new StepWriter();
        int a = w.Add("IFCMEMBER", "'G1',#1,'M',$,$,$,$,'20',$");
        int b = w.Add("IFCMEMBER", "'G2',#1,'M',$,$,$,$,'10',$");
        int memberType = w.Add("IFCMEMBERTYPE", "'GT',#1,'T',$,$,$,$,'500',$,.MULLION.");
        w.Add("IFCRELDEFINESBYTYPE", $"'GR',#1,$,$,(#{a},#{b}),#{memberType}");
        int p = w.Add("IFCPLATE", "'G3',#1,'P',$,$,$,$,'30',$");
        int plateType = w.Add("IFCPLATETYPE", "'GP',#1,'T',$,$,$,$,'30',$,.NOTDEFINED.");
        w.Add("IFCRELDEFINESBYTYPE", $"'GR2',#1,$,$,(#{p},#999),#{plateType}");
        var elementByEntity = new Dictionary<int, int> { [a] = 20, [b] = 10, [p] = 30 };
        string before = Text(w);

        Assert.Equal(0, IfcExporter.ApplyCurtainPanelTypeOrderCurrent(w, elementByEntity,
            new Dictionary<int, (int, int)> { [20] = (1, 0), [10] = (1, 1), [30] = (1, 2) }));

        Assert.Equal(before, Text(w));
    }
}
