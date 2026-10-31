namespace RvtIO.Internal;

/// <summary>Exact own VerticalRegionsStructure fields. Orientations/ids are native
/// values, not API layer indices or an IsVerticallyHomogeneous classification.</summary>
internal sealed record RawHostVerticalRegions(
    int[] ExtendableRegionIdsTop, int[] ExtendableRegionIdsBottom,
    RawRegionLayerPair[] RegionToLayerMap, RawGridSegment[] Segments,
    RawGridRegion[] Regions, double Tolerance, double SampleHeight,
    double CutOffHeight, byte PreviewDimensionsValid);

internal sealed record RawRegionLayerPair(int RegionId, int LayerId);
internal sealed record RawGridSegment(double Coordinate, int Id, int Orientation,
    int FirstRegionId, int SecondRegionId);
internal sealed record RawGridRegion(int Id, int[] SegmentIds);
