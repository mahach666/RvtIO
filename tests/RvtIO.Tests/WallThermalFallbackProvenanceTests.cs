using System.Reflection;
using System.Text.Json;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class WallThermalFallbackProvenanceTests
{
    private static readonly int[] ThermalIds = [-1005430, -1005436, -1005435, -1005434, -1005431];

    private static void Enrich(RevitFileInfo info, ParsingState? state) =>
        typeof(ElementBuilder).GetMethod("EnrichTypeLevelBips", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [info, state]);

    private static (RevitFileInfo Info, Element Wall) Model()
    {
        var wall = new Element { Id = 10, CategoryId = -2000011, References = [20],
            Parameters = [new Parameter { Id = -1001000, Value = 0.375, StorageType = StorageType.Double }] };
        var material = new Element { Id = 20, CategoryId = -2000700 };
        return (new RevitFileInfo { Elements = [wall, material] }, wall);
    }

    [Theory]
    [InlineData(-1005430, 3.4866666666666668, StorageType.Double)]
    [InlineData(-1005436, 3, StorageType.Integer)]
    [InlineData(-1005435, 0.7, StorageType.Double)]
    [InlineData(-1005434, 453330.0, StorageType.Double)]
    [InlineData(-1005431, 0.375 / 3.4866666666666668, StorageType.Double)]
    public void NewThermalFallbackRetainsLegacyValueButIsNotExact(int id, object expected, StorageType storage)
    {
        var (info, wall) = Model();
        Enrich(info, new ParsingState());
        var parameter = Assert.Single(wall.Parameters, p => p.Id == id);
        Assert.Equal(expected, parameter.Value);
        Assert.Equal(storage, parameter.StorageType);
        Assert.True(parameter.IsApproximate);
        Assert.Null(parameter.SpecType);
        Assert.Null(parameter.ConversionFactor);
        string serialized = JsonSerializer.Serialize(parameter);
        Enrich(info, new ParsingState());
        Assert.Same(parameter, Assert.Single(wall.Parameters, p => p.Id == id));
        Assert.Equal(serialized, JsonSerializer.Serialize(parameter));
        Assert.True(parameter.IsApproximate);
    }

    public static IEnumerable<object?[]> ExistingValues() => ThermalIds.SelectMany(id =>
        new object?[] { null, 0, 0.0, "{unresolved}", -1 }.SelectMany(value =>
            new[] { false, true }.Select(approximate => new object?[] { id, value, approximate })));

    [Theory]
    [MemberData(nameof(ExistingValues))]
    public void ExistingParameterIsNeverReplacedOrRemarked(int id, object? value, bool approximate)
    {
        var (info, wall) = Model();
        var parameter = new Parameter { Id = id, Value = value, IsApproximate = approximate,
            StorageType = StorageType.Double, Definition = new Definition { Id = id, Name = "own" },
            SpecType = "own-spec", UnitSymbol = "own-unit", ConversionFactor = 2.5 };
        wall.Parameters.Add(parameter);
        string before = JsonSerializer.Serialize(parameter);
        Enrich(info, new ParsingState());
        Assert.Same(parameter, Assert.Single(wall.Parameters, p => p.Id == id));
        Assert.Equal(before, JsonSerializer.Serialize(parameter));
        Assert.Equal(approximate, parameter.IsApproximate);
        Assert.Equal("own", parameter.Definition.Name);
        Assert.Equal("own-spec", parameter.SpecType);
        Assert.Equal("own-unit", parameter.UnitSymbol);
        Assert.Equal(2.5, parameter.ConversionFactor);
    }

    [Theory]
    [InlineData("null_state")]
    [InlineData("no_category")]
    [InlineData("nonwall")]
    [InlineData("no_material")]
    [InlineData("unresolved_material")]
    [InlineData("wrong_material_category")]
    public void ExistingFallbackScopeIsNotExpanded(string mutation)
    {
        var (info, wall) = Model();
        switch (mutation)
        {
            case "no_category": wall.CategoryId = null; break;
            case "nonwall": wall.CategoryId = -2000032; break;
            case "no_material": wall.References = []; break;
            case "unresolved_material": wall.References = [21]; break;
            case "wrong_material_category": info.Elements[1].CategoryId = -2000023; break;
        }
        Enrich(info, mutation == "null_state" ? null : new ParsingState());
        Assert.DoesNotContain(wall.Parameters, p => ThermalIds.Contains(p.Id));
    }

    [Theory]
    [InlineData(null, StorageType.Double)]
    [InlineData(1, StorageType.Integer)]
    public void ResistanceStillRequiresExistingDoubleWidth(object? width, StorageType storage)
    {
        var (info, wall) = Model();
        wall.Parameters[0].Value = width;
        wall.Parameters[0].StorageType = storage;
        Enrich(info, new ParsingState());
        Assert.DoesNotContain(wall.Parameters, p => p.Id == -1005431);
        Assert.All(wall.Parameters.Where(p => ThermalIds.Contains(p.Id)), p => Assert.True(p.IsApproximate));
    }
}
