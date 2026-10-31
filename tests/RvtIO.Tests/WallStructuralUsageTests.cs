using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class WallStructuralUsageTests
{
    [Theory]
    [InlineData(1, 0, 0)] // default stored value, non-structural flag: non-bearing
    [InlineData(1, 1, 1)] // default stored value, structural flag: bearing
    [InlineData(3, 1, 3)] // a stored value above the default is a real one
    [InlineData(0, 1, 1)] // a stored zero is not above the default
    public void StoredValueAboveTheDefaultWinsOtherwiseTheStructuralFlagDecides(int stored, int significant, int expected)
    {
        var wall = new Element { Id = 1 };
        wall.Parameters.Add(new Parameter { Id = -1001119, StorageType = StorageType.Integer, Value = stored });
        wall.Parameters.Add(new Parameter { Id = -1001596, StorageType = StorageType.Integer, Value = significant });
        ElementBuilder.FixWallStructuralUsage(new RevitFileInfo { Elements = { wall } });
        Assert.Equal(expected, wall.Parameters.Single(p => p.Id == -1001119).Value);
    }

    [Fact]
    public void WithoutTheFlagTheStoredValueStays()
    {
        var wall = new Element { Id = 1 };
        wall.Parameters.Add(new Parameter { Id = -1001119, StorageType = StorageType.Integer, Value = 3 });
        ElementBuilder.FixWallStructuralUsage(new RevitFileInfo { Elements = { wall } });
        Assert.Equal(3, wall.Parameters.Single().Value);
    }
}
