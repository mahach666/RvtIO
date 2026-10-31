using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

public class IfcExporterGhostFilterTests
{
    private static Element Wall(bool discovered) => new()
    {
        Id = 43,
        TypeId = 44,
        CategoryId = -2000011,
        Category = "OST_Walls",
        Name = "Axis wall",
        IsDiscovered = discovered,
        References = [99],
        Parameters =
        [
            new Parameter { Id = -1012100, StorageType = StorageType.Integer, Value = 99 },
            new Parameter { Id = -1001000, StorageType = StorageType.Double, Value = 1.0 },
            new Parameter { Id = -1001105, StorageType = StorageType.Double, Value = 3.0 },
        ],
        Location = new LocationCurve { CurveType = "Line", StartPoint = [10, 20, 5], EndPoint = [14, 20, 5] },
        BoundingBox = [10, 19, 5, 14, 21, 8],
    };

    private static string Export(bool discovered, bool exactTable, IfcProfile version)
    {
        var info = new RevitFileInfo { ElemTableIsExact = exactTable };
        info.Elements.Add(Wall(discovered));
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-ghost-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter { Profile = version }.Export(info, path);
            return File.ReadAllText(path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RecordOutsideAnExactElemTable_IsNotAnIfcProduct_InCurrent()
    {
        string ifc = Export(discovered: true, exactTable: true, IfcProfile.Current);
        Assert.DoesNotContain("=IFCWALL", ifc);
    }

    [Theory]
    [InlineData(true, false, IfcProfile.Current)]
    [InlineData(true, true, IfcProfile.Legacy)]
    [InlineData(false, true, IfcProfile.Current)]
    public void OtherwiseTheWallIsExported(bool discovered, bool exactTable, IfcProfile version)
    {
        string ifc = Export(discovered, exactTable, version);
        Assert.Contains("=IFCWALL", ifc);
    }
}
