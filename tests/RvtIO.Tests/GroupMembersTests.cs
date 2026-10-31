using RvtIO.Parsing;
using RvtIO;
using Xunit;

namespace RvtIO.Tests;

public class GroupMembersTests
{
    private static byte[] GroupBlock(params (int Id, int Index)[] pairs)
    {
        // [eid][hash][size][classIdx] [payload ... count pairs footer]; the payload starts at +14.
        int payload = 40 + 4 + 8 * pairs.Length;
        uint size = (uint)(payload + 2);
        var data = new byte[16 + size];
        BitConverter.GetBytes(7).CopyTo(data, 0);
        BitConverter.GetBytes(size).CopyTo(data, 8);
        BitConverter.GetBytes((ushort)1322).CopyTo(data, 12);
        int at = 14 + 40;
        BitConverter.GetBytes(pairs.Length).CopyTo(data, at); at += 4;
        foreach (var (id, index) in pairs)
        {
            BitConverter.GetBytes(id).CopyTo(data, at);
            BitConverter.GetBytes(index).CopyTo(data, at + 4);
            at += 8;
        }
        BitConverter.GetBytes(size).CopyTo(data, data.Length - 4);
        return data;
    }

    [Fact]
    public void GroupRoot_ListsItsMemberIdsInStoredOrder()
    {
        var data = GroupBlock((101, 4), (102, 5), (103, 9));
        Assert.Equal([101, 102, 103], TfBlockDeserializer.ExtractGroupMembers(data, 0, (uint)(data.Length - 16), 1322));
    }

    [Fact]
    public void ZeroBasedIndicesDoNotShortenTheMemberList()
    {
        var data = GroupBlock((101, 0), (102, 1), (103, 2), (104, 3), (105, 4));
        Assert.Equal(5, TfBlockDeserializer.ExtractGroupMembers(data, 0, (uint)(data.Length - 16), 1322)!.Length);
    }

    [Fact]
    public void OtherClassesOrBrokenFootersHaveNoMembers()
    {
        var data = GroupBlock((101, 4));
        Assert.Null(TfBlockDeserializer.ExtractGroupMembers(data, 0, (uint)(data.Length - 16), 1333));
        BitConverter.GetBytes(1).CopyTo(data, data.Length - 4);
        Assert.Null(TfBlockDeserializer.ExtractGroupMembers(data, 0, (uint)(data.Length - 16), 1322));
    }
}

public class HorizontalPolygonOverlapTests
{
    private static List<(double X, double Y)> Square(double x, double y, double size) =>
        [(x, y), (x + size, y), (x + size, y + size), (x, y + size)];

    [Fact]
    public void OverlappingSquares_Overlap()
        => Assert.True(Ifc.IfcExporter.HorizontalPolygonsOverlap(Square(0, 0, 4), Square(3, 3, 4), 0.5));

    [Fact]
    public void TouchingOrSeparateSquares_DoNotOverlap()
    {
        Assert.False(Ifc.IfcExporter.HorizontalPolygonsOverlap(Square(0, 0, 4), Square(4, 0, 4), 0.01));
        Assert.False(Ifc.IfcExporter.HorizontalPolygonsOverlap(Square(0, 0, 4), Square(10, 10, 4), 0.01));
    }

    [Fact]
    public void ConcavePolygon_UsesItsRealShape()
    {
        // An L shape does not cover the square cut out of its corner.
        List<(double X, double Y)> shape = [(0, 0), (6, 0), (6, 2), (2, 2), (2, 6), (0, 6)];
        Assert.False(Ifc.IfcExporter.HorizontalPolygonsOverlap(shape, Square(3, 3, 2), 0.01));
        Assert.True(Ifc.IfcExporter.HorizontalPolygonsOverlap(shape, Square(4, 0.5, 1), 0.5));
    }
}
