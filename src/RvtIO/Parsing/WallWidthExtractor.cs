using RvtIO.Internal;

namespace RvtIO.Parsing;

internal static class WallWidthExtractor
{
    /// <summary>Extract wall widths from 0xFFFF block Tf data (half-width anchor pattern).</summary>
    public static void ExtractFromBlocks(ParsingState state)
    {
        var nonRecordChunks = state.NonRecordChunks;
        var recordChunks = state.RecordChunks;
        var records = state.Records;
        var wallTypeWidths = state.WallTypeWidths;
        var elemBipDoubles = state.ElemBipDoubles;

        var wallInstWidths = new Dictionary<uint, (double width, uint typeId)>(); // instanceId to (width, typeId)
        foreach (var chunk in nonRecordChunks.Concat(recordChunks))
        {
            int pos = 0;
            while (pos + 16 <= chunk.Length)
            {
                uint eid = BitConverter.ToUInt32(chunk, pos);
                if (pos + 12 <= chunk.Length)
                {
                    uint rbs = BitConverter.ToUInt32(chunk, pos + 4);
                    ushort rm = BitConverter.ToUInt16(chunk, pos + 8);
                    if (rm == 0x053C && ElementIdEncoding.IsSupported(eid)
                        && rbs > 0 && rbs < 500_000 && pos + 12 + (int)rbs <= chunk.Length)
                    { pos += 12 + (int)rbs; continue; }
                }
                uint dsz = BitConverter.ToUInt32(chunk, pos + 8);
                if (dsz > 1_000_000) { pos++; continue; }
                if (eid == 0) { pos += 4; continue; }
                if (dsz == 0) { pos++; continue; }
                if (pos + 16 + (int)dsz > chunk.Length) break;
                uint ci = BitConverter.ToUInt32(chunk, pos + 12);
                if ((ci >> 16) == 0xFFFF && records.TryGetValue(eid, out var rec) && rec.CategoryId == -2000011)
                {
                    int dataStart = pos + 16;
                    int dataEnd = dataStart + (int)dsz;
                    for (int i = dataStart; i + 24 <= dataEnd; i += 2)
                    {
                        int wai = BitConverter.ToInt32(chunk, i);
                        int uli = BitConverter.ToInt32(chunk, i + 4);
                        int as2 = BitConverter.ToInt32(chunk, i + 8);
                        int at2 = BitConverter.ToInt32(chunk, i + 12);
                        int ab2 = BitConverter.ToInt32(chunk, i + 16);
                        int cf2 = BitConverter.ToInt32(chunk, i + 20);
                        if (wai <= 0) continue;
                        if (uli == 0) continue;
                        bool ap1 = as2 == -9 && at2 >= -100 && at2 <= 0 && ab2 >= -100 && ab2 <= 0;
                        bool ap2 = as2 == -1 && at2 == -1 && ab2 == -1;
                        if (!ap1 && !ap2) continue;
                        if (cf2 != -1) continue;
                        if (!wallInstWidths.ContainsKey(eid) && i - 16 >= dataStart)
                        {
                            double hw = BitConverter.ToDouble(chunk, i - 16);
                            if (hw > 0.001 && hw < 50 && !double.IsNaN(hw) && !double.IsInfinity(hw))
                                wallInstWidths[eid] = (hw * 2, (uint)wai);
                        }
                        break;
                    }
                }
                pos += 16 + (int)dsz;
            }
        }
        foreach (var (eid, (w, tid)) in wallInstWidths)
        {
            wallTypeWidths.TryAdd(tid, w);
            if (!elemBipDoubles.TryGetValue(eid, out var bd))
                elemBipDoubles[eid] = bd = new Dictionary<int, double>();
            bd.TryAdd(-1001000, w); // WALL_ATTR_WIDTH_PARAM
        }
        foreach (var (tid, w) in wallTypeWidths)
        {
            if (!elemBipDoubles.TryGetValue(tid, out var bd))
                elemBipDoubles[tid] = bd = new Dictionary<int, double>();
            bd.TryAdd(-1001000, w);
        }
        DiagnosticSink.WriteLine($"Wall widths extracted: {wallInstWidths.Count} instances, {wallTypeWidths.Count} types");
    }

    /// <summary>Extract wall widths from CompoundStructure Grid patterns near PreviewElemId.</summary>
    public static void ExtractFromGrid(ParsingState state)
    {
        var elemTable = state.ElemTable;
        var records = state.Records;
        var chunks = state.Chunks;
        var wallTypeWidths = state.WallTypeWidths;
        var elemBipDoubles = state.ElemBipDoubles;

        // Map PreviewElemId to WallTypeId. Wall type defs: TypeId 521 + CategoryId OST_Walls (-2000011) + has reference to TypeId 520 (preview)
        var wallTypeDefIds = new HashSet<uint>();
        var previewToWallType = new Dictionary<uint, uint>();
        foreach (var (eid, tid) in elemTable)
        {
            if (tid != 521) continue;
            if (!records.TryGetValue(eid, out var rec2)) continue;
            if (rec2.CategoryId != -2000011) continue; // must be OST_Walls
            foreach (var refId in rec2.References)
                if (elemTable.TryGetValue(refId, out var refTid) && refTid == 520)
                { previewToWallType[refId] = eid; wallTypeDefIds.Add(eid); break; }
        }
        DiagnosticSink.WriteLine($"Wall type defs: {wallTypeDefIds.Count}, PreviewElemId mappings: {previewToWallType.Count}");

        var previewPositions = new Dictionary<int, List<(int off, uint wtId)>>();
        for (int ci = 0; ci < chunks.Count; ci++)
        {
            var chunk = chunks[ci];
            for (int pOff = 0; pOff + 4 <= chunk.Length; pOff++)
            {
                uint pv = BitConverter.ToUInt32(chunk, pOff);
                if (!previewToWallType.TryGetValue(pv, out var wtId)) continue;
                if (!previewPositions.TryGetValue(ci, out var list))
                    previewPositions[ci] = list = new List<(int, uint)>();
                list.Add((pOff, wtId));
            }
        }

        var allGridMatches = new List<(uint wtId, double width, int nCoords, int dist, int chunkIdx)>();
        foreach (var (ci, positions) in previewPositions)
        {
            var chunk = chunks[ci];
            var sortedPreviews = positions.OrderBy(p => p.off).ToList();
            var gridsInChunk = new List<(int off, double width, int nCoords)>();
            for (int gOff = 0; gOff + 48 <= chunk.Length; gOff++)
            {
                var (tw, nc) = DetectGrid(chunk, gOff);
                if (nc >= 2)
                {
                    gridsInChunk.Add((gOff, tw, nc));
                    gOff += nc * 24 - 1; // skip past this Grid
                }
            }
            foreach (var (gOff, gw, gnc) in gridsInChunk)
            {
                (int off, uint wtId)? bestPreview = null;
                foreach (var (pOff, wtId) in sortedPreviews)
                {
                    int dist = gOff - pOff;
                    if (dist >= 300 && dist <= 1000)
                    {
                        if (bestPreview == null || dist < gOff - bestPreview.Value.off)
                            bestPreview = (pOff, wtId);
                    }
                }
                if (bestPreview != null)
                {
                    int d = gOff - bestPreview.Value.off;
                    allGridMatches.Add((bestPreview.Value.wtId, gw, gnc, d, ci));
                }
            }
        }

        // Filter bad chunks: more than 5 matches in a chunk sharing the same (rounded width, dist) are false positives
        var badChunks = new HashSet<int>();
        foreach (var g in allGridMatches.GroupBy(m => m.chunkIdx))
            if (g.GroupBy(m => (Math.Round(m.width, 4), m.dist)).Any(x => x.Count() > 5))
                badChunks.Add(g.Key);

        // Prefer most coords (=most layers), then closest distance
        var gridWidths = new Dictionary<uint, (double width, int nCoords, int dist, int chunkIdx)>();
        foreach (var g in allGridMatches.Where(m => !badChunks.Contains(m.chunkIdx)).GroupBy(m => m.wtId))
        {
            var best = g.OrderByDescending(m => m.nCoords).ThenBy(m => m.dist).First();
            gridWidths[g.Key] = (best.width, best.nCoords, best.dist, best.chunkIdx);
        }

        int gridAdded = 0;
        foreach (var (wtId, (w, nc, dist, ci2)) in gridWidths.OrderBy(x => x.Key))
        {
            // Skip weak Grid matches: only 2 coords (single layer) far from PreviewElemId
            if (nc <= 2 && dist > 700)
            {
                DiagnosticSink.WriteLine($"    wt {wtId}: SKIP {w:F6}ft/{w*304.8:F1}mm - weak grid({nc}c, dist={dist}, chunk[{ci2}])");
                continue;
            }
            DiagnosticSink.WriteLine($"    wt {wtId}: {w:F6}ft/{w*304.8:F1}mm - grid({nc}c, dist={dist}, chunk[{ci2}])");
            if (!wallTypeWidths.ContainsKey(wtId))
            {
                wallTypeWidths[wtId] = w;
                if (!elemBipDoubles.TryGetValue(wtId, out var bd))
                    elemBipDoubles[wtId] = bd = new Dictionary<int, double>();
                bd[-1001000] = w;
                gridAdded++;
            }
            else
            {
                DiagnosticSink.WriteLine($"      (already has instance width {wallTypeWidths[wtId]:F6}ft, keeping)");
            }
        }
        DiagnosticSink.WriteLine($"Grid widths: {gridAdded} types added (total wallTypeWidths: {wallTypeWidths.Count})");
        DiagnosticSink.WriteLine($"Unmatched wall type defs: {wallTypeDefIds.Count - wallTypeWidths.Count}");
    }

    /// <summary>Extract wall widths by scanning Grid patterns inside blocks belonging to wall type elements.</summary>
    /// <remarks>
    /// This solves the cross-chunk problem: instead of relying on PreviewElemId proximity,
    /// we scan each block's data for CompoundStructure Grid patterns and use the block's
    /// own elemId for association. Works for both ElemTable types and discovered types.
    /// </remarks>
    public static void ExtractFromBlockGrid(ParsingState state)
    {
        var records = state.Records;
        var elemTable = state.ElemTable;
        var wallTypeWidths = state.WallTypeWidths;
        var elemBipDoubles = state.ElemBipDoubles;

        // Wall type element IDs: CategoryId=OST_Walls AND no instance-level params
        // (wall types do not have WALL_USER_HEIGHT_PARAM in their BIP doubles)
        var wallTypeIds = new HashSet<uint>();
        foreach (var (eid, rec) in records)
        {
            if (rec.CategoryId != -2000011) continue; // OST_Walls only
            if (wallTypeWidths.ContainsKey(eid)) continue; // already have width
            if (state.ElemBipDoubles.TryGetValue(eid, out var bd) && bd.ContainsKey(-1001105)) continue; // WALL_USER_HEIGHT_PARAM means instance
            wallTypeIds.Add(eid);
        }
        foreach (var (eid, _) in elemTable)
        {
            if (wallTypeWidths.ContainsKey(eid)) continue;
            if (!records.TryGetValue(eid, out var rec)) continue;
            if (rec.CategoryId != -2000011) continue;
            if (state.ElemBipDoubles.TryGetValue(eid, out var bd2) && bd2.ContainsKey(-1001105)) continue;
            wallTypeIds.Add(eid);
        }

        if (wallTypeIds.Count == 0) return;

        var blockGridWidths = new Dictionary<uint, (double width, int nCoords)>();
        foreach (var chunk in state.NonRecordChunks.Concat(state.RecordChunks))
        {
            int pos = 0;
            while (pos + 16 <= chunk.Length)
            {
                uint eid = BitConverter.ToUInt32(chunk, pos);
                if (pos + 12 <= chunk.Length)
                {
                    uint rbs = BitConverter.ToUInt32(chunk, pos + 4);
                    ushort rm = BitConverter.ToUInt16(chunk, pos + 8);
                    if (rm == 0x053C && ElementIdEncoding.IsSupported(eid)
                        && rbs > 0 && rbs < 500_000 && pos + 12 + (int)rbs <= chunk.Length)
                    { pos += 12 + (int)rbs; continue; }
                }
                uint dsz = BitConverter.ToUInt32(chunk, pos + 8);
                if (dsz > 1_000_000) { pos++; continue; }
                if (eid == 0) { pos += 4; continue; }
                if (dsz == 0) { pos++; continue; }
                if (pos + 16 + (int)dsz > chunk.Length) break;

                if (wallTypeIds.Contains(eid) && dsz >= 48)
                {
                    int dataStart = pos + 16;
                    int dataEnd = dataStart + (int)dsz;
                    for (int g = dataStart; g + 48 <= dataEnd; g++)
                    {
                        var (tw, nc) = DetectGrid(chunk, g);
                        if (nc >= 2)
                        {
                            if (!blockGridWidths.TryGetValue(eid, out var existing) || nc > existing.nCoords)
                                blockGridWidths[eid] = (tw, nc);
                            g += nc * 24 - 1; // skip past
                        }
                    }
                }
                pos += 16 + (int)dsz;
            }
        }

        // CompoundStructure may also be in a record body, after the refs
        foreach (var (eid, rec) in records)
        {
            if (!wallTypeIds.Contains(eid)) continue;
            if (blockGridWidths.ContainsKey(eid)) continue; // already found in block
            var body = rec.Body;
            if (body.Length < 48) continue;
            for (int g = 0; g + 48 <= body.Length; g++)
            {
                var (tw, nc) = DetectGrid(body, g);
                if (nc >= 2)
                {
                    if (!blockGridWidths.TryGetValue(eid, out var existing) || nc > existing.nCoords)
                        blockGridWidths[eid] = (tw, nc);
                    g += nc * 24 - 1;
                }
            }
        }

        int added = 0;
        foreach (var (eid, (w, nc)) in blockGridWidths.OrderBy(x => x.Key))
        {
            if (wallTypeWidths.ContainsKey(eid)) continue;
            DiagnosticSink.WriteLine($"    wt {eid}: {w:F6}ft/{w * 304.8:F1}mm - block-grid({nc}c)");
            wallTypeWidths[eid] = w;
            if (!elemBipDoubles.TryGetValue(eid, out var bd))
                elemBipDoubles[eid] = bd = new Dictionary<int, double>();
            bd[-1001000] = w;
            added++;
        }
        DiagnosticSink.WriteLine($"Block-grid widths: {added} types added (total wallTypeWidths: {wallTypeWidths.Count})");
    }

    private static (double totalW, int nCoords) DetectGrid(byte[] chunk, int off)
    {
        if (off + 48 > chunk.Length) return (0, 0);
        if (BitConverter.ToInt64(chunk, off) != 0) return (0, 0);       // Coord[0]=0.0
        if (BitConverter.ToUInt32(chunk, off + 8) != 0) return (0, 0);  // Id[0]=0
        if (BitConverter.ToUInt32(chunk, off + 12) != 0) return (0, 0); // Orient[0]=0
        double v1 = BitConverter.ToDouble(chunk, off + 24);
        if (v1 <= 0.003 || v1 >= 5.0 || double.IsNaN(v1) || double.IsInfinity(v1)) return (0, 0);
        var coords = new List<double> { 0.0, v1 };
        int nOff = off + 48;
        while (nOff + 8 <= chunk.Length && coords.Count < 30)
        {
            double vn = BitConverter.ToDouble(chunk, nOff);
            if (vn <= coords[^1] + 0.0005 || vn >= 10.0 || double.IsNaN(vn) || double.IsInfinity(vn)) break;
            coords.Add(vn);
            nOff += 24;
        }
        if (coords.Count < 2 || coords[^1] < 0.01 || coords[^1] > 10) return (0, 0);
        return (coords[^1], coords.Count);
    }
}
