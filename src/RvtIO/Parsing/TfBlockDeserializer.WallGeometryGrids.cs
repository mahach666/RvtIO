namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record WallGridCell(int U, int V, WallRawIntList EdgeTags, int FaceTag,
        int UGridline, int UGridline2, int VGridline, int VGridline2, int PanelId, int Start, int End);
    internal sealed record WallGridCellMap(uint Count, int Start, int End, IReadOnlyList<WallGridCell> Entries);
    internal sealed record WallGridEdgePair(int First, int Second, int Start, int End);
    internal sealed record WallGridEdgeMap(uint Count, int Start, int End, IReadOnlyList<WallGridEdgePair> Entries);
    internal sealed record WallGridEdgeSegment(long StartParameterBits, long EndParameterBits,
        int EdgeTag, int Start, int End);
    internal sealed record WallGridOriginalEdge(int OriginalEdge, uint SegmentCount,
        int SegmentsStart, int SegmentsEnd, IReadOnlyList<WallGridEdgeSegment> Segments, int Start, int End);
    internal sealed record WallGridEdgeSegments(uint Count, int Start, int End, IReadOnlyList<WallGridOriginalEdge> Entries);
    internal sealed record WallGridOwnTail(int QueueIndex, int Start, int End, int DriverId,
        WallGridCellMap Cells, WallGridEdgeMap EdgeMap, WallGridEdgeSegments EdgeSegments);

    private static bool HasWallGridOwnTailSchema()
    {
        var fields = SerializationFields(590);
        var inherited = SerializationFields(96);
        if (fields is not { Count: 12 } || inherited is not { Count: 8 }
            || !fields.Take(8).SequenceEqual(inherited)
            || fields[8] is not { Declaring: 590, Name: "m_driverId", Type: 4, Mods: 0, Ref: null, Cnt: null }
            || fields[9] is not { Declaring: 590, Name: "m_cellMap", Type: 14, Mods: 80, Ref: null, Cnt: null }
            || fields[10] is not { Declaring: 590, Name: "m_edgeToOriginalEdgeMap", Type: 14, Mods: 80, Ref: 221, Cnt: null }
            || fields[11] is not { Declaring: 590, Name: "m_originalToEdgeSegs", Type: 14, Mods: 80, Ref: null, Cnt: null })
            return false;
        var coords = SerializationFields(584);
        var contents = SerializationFields(583);
        var pair = SerializationFields(221);
        if (coords is not { Count: 2 } || contents is not { Count: 7 } || pair is not { Count: 2 }
            || coords[0] is not { Declaring: 584, Name: "m_u", Type: 4, Mods: 0, Ref: null, Cnt: null }
            || coords[1] is not { Declaring: 584, Name: "m_v", Type: 4, Mods: 0, Ref: null, Cnt: null }
            || contents[0] is not { Declaring: 583, Name: "m_edgeTags", Type: 4, Mods: 80, Ref: null, Cnt: null }
            || contents[1] is not { Declaring: 583, Name: "m_faceTag", Type: 4, Mods: 0, Ref: null, Cnt: null }
            || pair[0] is not { Declaring: 221, Name: "first", Type: 4, Mods: 0, Ref: null, Cnt: null }
            || pair[1] is not { Declaring: 221, Name: "second", Type: 4, Mods: 0, Ref: 221, Cnt: null }) return false;
        string[] names = ["m_uGridline", "m_uGridline2", "m_vGridline", "m_vGridline2", "m_panelId"];
        for (int i = 0; i < names.Length; i++)
            if (contents[i + 2] is not { Declaring: 583, Type: 14, Mods: 0, Ref: 20, Cnt: null }
                || contents[i + 2].Name != names[i]) return false;
        return true;
    }

    private static WallGridOwnTail ReadWallGridOwnTail(Cursor c, int start, int index)
    {
        int first = c.Pos;
        int driver = ReadI32(c), cellsStart = c.Pos;
        uint cellCount = ReadWallOwnCount(c, 36);
        var cells = new List<WallGridCell>();
        if (cellCount != uint.MaxValue)
            for (uint i = 0; i < cellCount; i++)
            {
                int rowStart = c.Pos;
                int u = ReadI32(c), v = ReadI32(c);
                var tags = ReadWallRawIntList(c, start);
                cells.Add(new(u, v, tags, ReadI32(c), ReadI32(c), ReadI32(c), ReadI32(c),
                    ReadI32(c), ReadI32(c), rowStart - start, c.Pos - start));
            }
        var cellMap = new WallGridCellMap(cellCount, cellsStart - start, c.Pos - start, cells);
        int edgesStart = c.Pos;
        uint edgeCount = ReadWallOwnCount(c, 8);
        var edges = new List<WallGridEdgePair>();
        if (edgeCount != uint.MaxValue)
            for (uint i = 0; i < edgeCount; i++)
            {
                int rowStart = c.Pos;
                edges.Add(new(ReadI32(c), ReadI32(c), rowStart - start, c.Pos - start));
            }
        var edgeMap = new WallGridEdgeMap(edgeCount, edgesStart - start, c.Pos - start, edges);
        int originalsStart = c.Pos;
        uint originalCount = ReadWallOwnCount(c, 8);
        var originals = new List<WallGridOriginalEdge>();
        if (originalCount != uint.MaxValue)
            for (uint i = 0; i < originalCount; i++)
            {
                int rowStart = c.Pos;
                int edge = ReadI32(c), segmentsStart = c.Pos;
                uint count = ReadWallOwnCount(c, 20);
                var segments = new List<WallGridEdgeSegment>();
                if (count != uint.MaxValue)
                    for (uint j = 0; j < count; j++)
                    {
                        int segmentStart = c.Pos;
                        // The private CEdgeSeg descriptor declares double m_start/m_end,
                        // then signed m_edgeTag: 20 persisted bytes, not the native size 24.
                        // Raw bits keep NaN payloads, infinities and signed zero.
                        if (!c.Take(16)) throw Fail;
                        long a = BitConverter.ToInt64(c.D, c.Pos - 16), b = BitConverter.ToInt64(c.D, c.Pos - 8);
                        segments.Add(new(a, b, ReadI32(c), segmentStart - start, c.Pos - start));
                    }
                originals.Add(new(edge, count, segmentsStart - start, c.Pos - start, segments,
                    rowStart - start, c.Pos - start));
            }
        var originalMap = new WallGridEdgeSegments(originalCount, originalsStart - start, c.Pos - start, originals);
        return new(index, first - start, c.Pos - start, driver, cellMap, edgeMap, originalMap);
    }
}
