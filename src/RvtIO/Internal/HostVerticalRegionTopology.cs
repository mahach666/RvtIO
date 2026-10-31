namespace RvtIO.Internal;

/// <summary>A sufficient exact grid partition test, not the Revit API classifier.
/// Only one positive rectangular region per native layer is supported here.
/// Sweeps, layer width getters, wall-height rescaling and IFC units are not inferred.</summary>
internal static class HostVerticalRegionTopology
{
    // Values of the RectangularGridSegmentOrientation enum of Revit 2022.
    private const int Vertical = 0, Horizontal = 1;

    internal sealed record ParallelLayerRegion(int LayerId, int RegionId,
        double MinU, double MaxU);

    internal static bool TryConfirmParallelRectangularLayers(
        RawHostVerticalRegions? grid, IReadOnlyList<RawCompoundStructureLayer>? layers,
        out ParallelLayerRegion[] validation)
    {
        validation = [];
        if (grid == null || layers == null || layers.Count is 0 or > 100_000
            || grid.Segments == null || grid.Regions == null || grid.RegionToLayerMap == null
            || grid.ExtendableRegionIdsTop == null || grid.ExtendableRegionIdsBottom == null
            || grid.Segments.Length > 100_000 || grid.Regions.Length != layers.Count
            || grid.RegionToLayerMap.Length != layers.Count
            || !double.IsFinite(grid.SampleHeight) || grid.SampleHeight <= 0
            || !double.IsFinite(grid.Tolerance) || grid.Tolerance < 0) return false;

        var regions = new Dictionary<int, RawGridRegion>();
        foreach (var region in grid.Regions)
            if (region == null || region.Id < 0 || region.SegmentIds == null
                || region.SegmentIds.Length != 4 || region.SegmentIds.Distinct().Count() != 4
                || !regions.TryAdd(region.Id, region)) return false;
        var segments = new Dictionary<int, RawGridSegment>();
        foreach (var segment in grid.Segments)
            if (segment == null || segment.Id < 0 || !double.IsFinite(segment.Coordinate)
                || segment.Orientation is not (Vertical or Horizontal)
                || segment.FirstRegionId == segment.SecondRegionId
                || !segments.TryAdd(segment.Id, segment)) return false;
        var regionByLayer = new Dictionary<int, int>();
        var mappedRegions = new HashSet<int>();
        foreach (var pair in grid.RegionToLayerMap)
            if (pair == null || pair.LayerId < 0 || !regions.ContainsKey(pair.RegionId)
                || !mappedRegions.Add(pair.RegionId)
                || !regionByLayer.TryAdd(pair.LayerId, pair.RegionId)) return false;
        if (!ValidExtensionSet(grid.ExtendableRegionIdsTop)
            || !ValidExtensionSet(grid.ExtendableRegionIdsBottom)) return false;

        // Reciprocal adjacency is checked both ways: neither dangling neighbors nor
        // unreferenced/extra segments can hide behind matching envelope coordinates.
        foreach (var segment in segments.Values)
        {
            if (!ValidNeighbor(segment.FirstRegionId, segment.Id)
                || !ValidNeighbor(segment.SecondRegionId, segment.Id)) return false;
            if (segment.Orientation == Horizontal
                && segment.FirstRegionId != -1 && segment.SecondRegionId != -1) return false;
        }
        var rectangles = new Dictionary<int, (RawGridSegment Low, RawGridSegment High)>();
        foreach (var region in regions.Values)
        {
            var vertical = new List<RawGridSegment>();
            var horizontal = new List<RawGridSegment>();
            foreach (int id in region.SegmentIds)
            {
                if (!segments.TryGetValue(id, out var segment)
                    || segment.FirstRegionId != region.Id && segment.SecondRegionId != region.Id) return false;
                (segment.Orientation == Vertical ? vertical : horizontal).Add(segment);
            }
            if (vertical.Count != 2 || horizontal.Count != 2) return false;
            vertical.Sort((a, b) => a.Coordinate.CompareTo(b.Coordinate));
            horizontal.Sort((a, b) => a.Coordinate.CompareTo(b.Coordinate));
            double width = vertical[1].Coordinate - vertical[0].Coordinate;
            if (!double.IsFinite(width) || width <= 0 || horizontal[0].Coordinate != 0
                || horizontal[1].Coordinate != grid.SampleHeight) return false;
            rectangles.Add(region.Id, (vertical[0], vertical[1]));
        }

        var result = new ParallelLayerRegion[layers.Count];
        var nativeIds = new HashSet<int>();
        int direction = 0;
        for (int index = 0; index < layers.Count; index++)
        {
            var layer = layers[index];
            if (layer == null || !nativeIds.Add(layer.RawLayerId)
                || !regionByLayer.TryGetValue(layer.RawLayerId, out int regionId)) return false;
            var rectangle = rectangles[regionId];
            result[index] = new(layer.RawLayerId, regionId, rectangle.Low.Coordinate, rectangle.High.Coordinate);
            if (index == 0) continue;
            var previous = rectangles[result[index - 1].RegionId];
            int nextDirection = previous.High.Id == rectangle.Low.Id ? 1
                : previous.Low.Id == rectangle.High.Id ? -1 : 0;
            if (nextDirection == 0 || direction != 0 && direction != nextDirection) return false;
            direction = nextDirection;
        }
        var first = rectangles[result[0].RegionId];
        var last = rectangles[result[^1].RegionId];
        if (!IsExterior(direction >= 0 ? first.Low : first.High)
            || !IsExterior(direction >= 0 ? last.High : last.Low)) return false;
        validation = result;
        return true;

        bool ValidNeighbor(int regionId, int segmentId) => regionId == -1
            || regions.TryGetValue(regionId, out var region) && region.SegmentIds.Contains(segmentId);
        bool ValidExtensionSet(int[] ids) => ids.Distinct().Count() == ids.Length
            && ids.All(regions.ContainsKey);
        static bool IsExterior(RawGridSegment segment) => segment.FirstRegionId == -1 || segment.SecondRegionId == -1;
    }
}
