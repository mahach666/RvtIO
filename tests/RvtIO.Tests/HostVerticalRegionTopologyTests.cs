using RvtIO.Internal;
using Xunit;

namespace RvtIO.Tests;

public class HostVerticalRegionTopologyTests
{
    [Fact]
    public void ExactReciprocalPartitionReturnsNativeLayerOrderNotGridEnumerationOrder()
    {
        var grid = Grid();
        Assert.True(HostVerticalRegionTopology.TryConfirmParallelRectangularLayers(grid, Layers(), out var validation));
        Assert.Equal(new[] { new HostVerticalRegionTopology.ParallelLayerRegion(7, 30, -0.25, 0), new(9, 40, 0, 0.125) }, validation);
        Assert.True(HostVerticalRegionTopology.TryConfirmParallelRectangularLayers(grid, Layers().AsEnumerable().Reverse().ToArray(), out validation));
        Assert.Equal(new[] { 9, 7 }, validation.Select(row => row.LayerId));
    }

    [Fact]
    public void WidthOrToleranceIsNotUsedToGuessApiGetterValuesOrRepairCoordinates()
    {
        var layers = Layers();
        layers[0] = layers[0] with { LayerWidthBits = BitConverter.DoubleToInt64Bits(123) };
        Assert.True(HostVerticalRegionTopology.TryConfirmParallelRectangularLayers(Grid(), layers, out var validation));
        Assert.Equal(0.25, validation[0].MaxU - validation[0].MinU);
        var grid = Grid();
        grid.Segments[4] = grid.Segments[4] with { Coordinate = Math.BitIncrement(3) };
        Assert.False(HostVerticalRegionTopology.TryConfirmParallelRectangularLayers(grid, layers, out _));
    }

    [Theory]
    [InlineData("duplicate_segment")] [InlineData("duplicate_region")] [InlineData("duplicate_region_map")]
    [InlineData("duplicate_layer_map")] [InlineData("duplicate_native_layer")]
    [InlineData("unknown_layer")] [InlineData("unknown_segment")] [InlineData("unknown_neighbor")]
    [InlineData("missing_reciprocal_neighbor")] [InlineData("duplicate_boundary")]
    [InlineData("negative_region")] [InlineData("negative_segment")] [InlineData("unsupported_orientation")]
    [InlineData("self_neighbor")] [InlineData("two_exteriors")]
    [InlineData("shared_horizontal")] [InlineData("extra_segment")]
    [InlineData("different_native_boundary_same_coordinate")] [InlineData("horizontal_break")]
    [InlineData("zero_width")] [InlineData("unknown_extension")] [InlineData("duplicate_extension")]
    [InlineData("missing_layer")] [InlineData("height_nan")] [InlineData("coordinate_infinity")]
    [InlineData("tolerance_nan")] [InlineData("negative_tolerance")]
    public void AmbiguousIncompleteOrNonparallelSourceIsRejectedWithoutPartialValidation(string mode)
    {
        var grid = Grid(); var layers = Layers();
        switch (mode)
        {
            case "duplicate_segment": grid.Segments[2] = grid.Segments[0]; break;
            case "duplicate_region": grid.Regions[1] = grid.Regions[0]; break;
            case "duplicate_region_map": grid.RegionToLayerMap[0] = grid.RegionToLayerMap[1]; break;
            case "duplicate_layer_map": grid.RegionToLayerMap[0] = new(40, 7); break;
            case "duplicate_native_layer": layers[1] = layers[0]; break;
            case "unknown_layer": layers[1] = layers[1] with { RawLayerId = 99 }; break;
            case "unknown_segment": grid.Regions[0].SegmentIds[0] = 999; break;
            case "unknown_neighbor": grid.Segments[1] = grid.Segments[1] with { SecondRegionId = 999 }; break;
            case "missing_reciprocal_neighbor": grid.Regions[1].SegmentIds[1] = 3; break;
            case "duplicate_boundary": grid.Regions[0].SegmentIds[0] = 3; break;
            case "negative_region": grid.Regions[0] = grid.Regions[0] with { Id = -1 }; break;
            case "negative_segment": grid.Segments[0] = grid.Segments[0] with { Id = -1 }; break;
            case "unsupported_orientation": grid.Segments[0] = grid.Segments[0] with { Orientation = 2 }; break;
            case "self_neighbor": grid.Segments[1] = grid.Segments[1] with { SecondRegionId = 30 }; break;
            case "two_exteriors": grid.Segments[0] = grid.Segments[0] with { FirstRegionId = -1 }; break;
            case "shared_horizontal": grid.Segments[3] = grid.Segments[3] with { SecondRegionId = 40 }; break;
            case "extra_segment": grid = grid with { Segments = [.. grid.Segments, new(0.5, 99, 0, 30, -1)] }; break;
            case "different_native_boundary_same_coordinate":
                grid.Segments[1] = grid.Segments[1] with { SecondRegionId = -1 };
                grid.Regions[1].SegmentIds[1] = 99;
                grid = grid with { Segments = [.. grid.Segments, new(0, 99, 0, 40, -1)] }; break;
            case "horizontal_break": grid.Segments[4] = grid.Segments[4] with { Coordinate = 2.5 }; break;
            case "zero_width": grid.Segments[0] = grid.Segments[0] with { Coordinate = 0 }; break;
            case "unknown_extension": grid = grid with { ExtendableRegionIdsTop = [999] }; break;
            case "duplicate_extension": grid = grid with { ExtendableRegionIdsBottom = [30, 30] }; break;
            case "missing_layer": layers = [layers[0]]; break;
            case "height_nan": grid = grid with { SampleHeight = double.NaN }; break;
            case "coordinate_infinity": grid.Segments[0] = grid.Segments[0] with { Coordinate = double.PositiveInfinity }; break;
            case "tolerance_nan": grid = grid with { Tolerance = double.NaN }; break;
            case "negative_tolerance": grid = grid with { Tolerance = -1 }; break;
        }
        Assert.False(HostVerticalRegionTopology.TryConfirmParallelRectangularLayers(grid, layers, out var validation));
        Assert.Empty(validation);
    }

    [Fact]
    public void EmptyMissingAndOverflowingWidthsAreUnvalidated()
    {
        Assert.False(HostVerticalRegionTopology.TryConfirmParallelRectangularLayers(null, Layers(), out _));
        Assert.False(HostVerticalRegionTopology.TryConfirmParallelRectangularLayers(Grid(), null, out _));
        Assert.False(HostVerticalRegionTopology.TryConfirmParallelRectangularLayers(Grid(), [], out _));
        var grid = Grid();
        grid.Segments[0] = grid.Segments[0] with { Coordinate = -double.MaxValue };
        grid.Segments[1] = grid.Segments[1] with { Coordinate = double.MaxValue };
        Assert.False(HostVerticalRegionTopology.TryConfirmParallelRectangularLayers(grid, Layers(), out _));
    }

    private static RawCompoundStructureLayer[] Layers() => [
        new(BitConverter.DoubleToInt64Bits(0.25), 1, -1, 700, -1, 7, 1),
        new(BitConverter.DoubleToInt64Bits(0.125), 2, -1, 701, -1, 9, 1)];

    private static RawHostVerticalRegions Grid() => new([40], [30], [new(40, 9), new(30, 7)], [
        new(-0.25, 3, 0, 30, -1), new(0, 4, 0, 30, 40), new(0.125, 16, 0, 40, -1),
        new(0, 14, 1, 30, -1), new(3, 15, 1, 30, -1),
        new(0, 17, 1, 40, -1), new(3, 18, 1, 40, -1)], [
        new(30, [15, 3, 14, 4]), new(40, [17, 4, 18, 16])], 1e-9, 3, 2.5, 1);
}
