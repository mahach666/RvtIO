using OpenMcdf;
using RvtIO.Parsing;

namespace RvtIO.Cli;

/// <summary>Diagnostic hex dump for blocks of specific elements.</summary>
static class DiagDump
{
    public static void Run(string rvtPath, HashSet<uint> targetIds)
    {
        using var root = RootStorage.OpenRead(rvtPath);

        var partStor = root.OpenStorage("Partitions");
        var partEntries = partStor.EnumerateEntries()
            .Where(e => e.Type == EntryType.Stream)
            .OrderByDescending(e => e.Length)
            .ToList();
        using var partStream = partStor.OpenStream(partEntries[0].Name);
        var raw = new byte[partStream.Length];
        partStream.ReadExactly(raw);

        var decompressor = new ChunkDecompressor();
        var chunks = decompressor.DecompressAll(raw);
        Console.Error.WriteLine($"Chunks: {chunks.Count}");

        int totalBlocks = 0;
        int chunkIdx = 0;
        foreach (var chunk in chunks)
        {
            int pos = 0;
            while (pos + 16 <= chunk.Length)
            {
                uint eid = BitConverter.ToUInt32(chunk, pos);
                uint hash = BitConverter.ToUInt32(chunk, pos + 4);
                int dataSize = BitConverter.ToInt32(chunk, pos + 8);
                uint classIdx = BitConverter.ToUInt32(chunk, pos + 12);

                if (eid == 0 || eid > 50_000_000 || dataSize <= 0 || dataSize > chunk.Length - pos - 16)
                    break;

                int dataStart = pos + 16;
                int dataEnd = dataStart + dataSize;
                totalBlocks++;

                if (targetIds.Contains(eid))
                {
                    bool isFFFF = (classIdx >> 16) == 0xFFFF;
                    bool isGRep = (classIdx & 0xFFFF) == 0x07D7;
                    bool is0702 = (classIdx & 0xFFFF) == 0x0702;
                    string tag = isFFFF ? "0xFFFF" : isGRep ? "GRep" : is0702 ? "0x702" : $"cls";

                    if (!isFFFF && !isGRep)
                    {
                        Console.WriteLine($"\n--- ElemId={eid} classIdx=0x{classIdx:X8} ({tag}) size={dataSize} chunk[{chunkIdx}] (skipping non-0xFFFF) ---");
                    }
                    else if (isGRep)
                    {
                        Console.WriteLine($"\n=== ElemId={eid} classIdx=0x{classIdx:X8} (GRep) size={dataSize} chunk[{chunkIdx}] ===");
                        DumpGRep(chunk, dataStart, dataEnd);
                    }
                    else
                    {
                        Console.WriteLine($"\n=== ElemId={eid} classIdx=0x{classIdx:X8} ({tag}) size={dataSize} chunk[{chunkIdx}] ===");
                        int dumpLen = Math.Min(dataSize, 512);
                        HexDump(chunk, dataStart, dumpLen);
                        if (dataSize > 512)
                            Console.WriteLine($"  ... ({dataSize - 512} more bytes)");
                        InterpretFFFFBlock(chunk, dataStart, dataEnd, eid);
                    }
                }

                pos = dataEnd;
            }
            chunkIdx++;
        }
        Console.Error.WriteLine($"Total blocks: {totalBlocks}");
    }

    static void HexDump(byte[] d, int start, int len)
    {
        for (int r = 0; r < len; r += 16)
        {
            Console.Write($"  {r:X4}: ");
            for (int c = 0; c < 16 && r + c < len; c++)
            {
                Console.Write($"{d[start + r + c]:X2} ");
                if (c == 7) Console.Write(" ");
            }
            int rem = Math.Min(16, len - r);
            for (int c = rem; c < 16; c++)
            {
                Console.Write("   ");
                if (c == 7) Console.Write(" ");
            }
            Console.Write(" |");
            for (int c = 0; c < 16 && r + c < len; c++)
            {
                byte b = d[start + r + c];
                Console.Write(b >= 0x20 && b < 0x7F ? (char)b : '.');
            }
            Console.WriteLine("|");
        }
    }

    static void InterpretFFFFBlock(byte[] d, int start, int end, uint eid)
    {
        for (int i = start; i + 32 <= end; i += 2)
        {
            if (BitConverter.ToUInt32(d, i) != eid) continue;
            int un = BitConverter.ToInt32(d, i + 12);
            int ow = BitConverter.ToInt32(d, i + 16);
            if (un != -1 || ow != -1) continue;

            int off = i - start;
            Console.WriteLine($"\n  --- Base fields at offset 0x{off:X3} ---");
            Console.WriteLine($"  [+00] m_id             = {BitConverter.ToInt32(d, i)}");
            Console.WriteLine($"  [+04] m_assocLevelId   = {BitConverter.ToInt32(d, i + 4)}");
            Console.WriteLine($"  [+08] m_famId          = {BitConverter.ToInt32(d, i + 8)}");
            Console.WriteLine($"  [+0C] m_unplacedOwnId  = {BitConverter.ToInt32(d, i + 12)}");
            Console.WriteLine($"  [+10] m_ownerDBViewId  = {BitConverter.ToInt32(d, i + 16)}");
            Console.WriteLine($"  [+14] m_createdPhaseId = {BitConverter.ToInt32(d, i + 20)}");
            Console.WriteLine($"  [+18] m_demolishedPhId = {BitConverter.ToInt32(d, i + 24)}");
            Console.WriteLine($"  [+1C] m_designOptionId = {BitConverter.ToInt32(d, i + 28)}");

            int after = i + 32;
            if (after + 3 <= end)
            {
                Console.WriteLine($"  [+20] m_locked         = {d[after]}");
                Console.WriteLine($"  [+21] m_moribund       = {d[after + 1]}");
                Console.WriteLine($"  [+22] m_dummy          = {d[after + 2]}");
            }

            ScanForWallAnchor(d, start, end);

            // Scan for null-ref markers: [FFFFFFFF:4][tag:2] where tag != FFFF
            Console.WriteLine($"\n  --- Null-ref markers [FFFFFFFF][tag:2] ---");
            for (int j = start; j + 6 <= end; j++)
            {
                if (BitConverter.ToInt32(d, j) == -1)
                {
                    ushort tag = BitConverter.ToUInt16(d, j + 4);
                    if (tag > 0 && tag != 0xFFFF && tag < 0x2000)
                    {
                        Console.WriteLine($"    offset 0x{j - start:X3}: FFFFFFFF + tag=0x{tag:X4}");
                    }
                }
            }

            DecodeAfterBase(d, start, end, off + 35); // base + 32 int bytes + 3 bools

            return;
        }
        Console.WriteLine("  (no base field anchor found)");
    }

    static void DecodeAfterBase(byte[] d, int start, int end, int afterBase)
    {
        int pos = start + afterBase;
        Console.WriteLine($"\n  --- Decoding Element fields 21-24 from offset 0x{afterBase:X3} ---");

        // Field 21: m_VSTAMacroProjZip (i16 array)
        if (pos + 4 > end) return;
        int zipCount = BitConverter.ToInt32(d, pos);
        if (zipCount == -1) { Console.WriteLine($"    [0x{pos-start:X3}] m_VSTAMacroProjZip = null (-1)"); pos += 4; }
        else if (zipCount >= 0 && zipCount < 10000) { Console.WriteLine($"    [0x{pos-start:X3}] m_VSTAMacroProjZip count={zipCount}"); pos += 4 + zipCount * 2; }
        else { Console.WriteLine($"    [0x{pos-start:X3}] m_VSTAMacroProjZip = ??? ({zipCount})"); return; }

        // Field 22: m_ElemProjDate (i32)
        if (pos + 4 > end) return;
        Console.WriteLine($"    [0x{pos-start:X3}] m_ElemProjDate = {BitConverter.ToInt32(d, pos)}");
        pos += 4;

        // Field 23: m_RevitVSTAVersion (i32)
        if (pos + 4 > end) return;
        Console.WriteLine($"    [0x{pos-start:X3}] m_RevitVSTAVersion = {BitConverter.ToInt32(d, pos)}");
        pos += 4;

        // Field 24: m_fileName (string - length-prefixed)
        if (pos + 4 > end) return;
        int strLen = BitConverter.ToInt32(d, pos);
        if (strLen == -1) { Console.WriteLine($"    [0x{pos-start:X3}] m_fileName = null"); pos += 4; }
        else if (strLen >= 0 && strLen < 10000) { Console.WriteLine($"    [0x{pos-start:X3}] m_fileName length={strLen}"); pos += 4 + strLen * 2; }
        else { Console.WriteLine($"    [0x{pos-start:X3}] m_fileName = ??? ({strLen})"); return; }

        Console.WriteLine($"\n  --- HostObj fields from offset 0x{pos-start:X3} ---");
        // HostObj field 0: m_hostObjMiscData (ref)
        // HostObj field 1: m_oGeomToElemMap (ref)
        // HostObj field 2: m_roomBounding (bool)
        // Refs: FFFFFFFF is null (4 bytes), otherwise inline data (variable)
        Console.Write("    Next 40 bytes: ");
        for (int j = 0; j < 40 && pos + j < end; j++)
            Console.Write($"{d[pos + j]:X2} ");
        Console.WriteLine();

        // Layout A: null ref = [FFFFFFFF:4] (no tag), non-null = [data...][tag:2]
        // Layout B: null ref = [FFFFFFFF:4][tag:2], non-null = [data...]
        if (pos + 4 <= end && BitConverter.ToInt32(d, pos) == -1)
        {
            Console.WriteLine($"    [0x{pos-start:X3}] m_hostObjMiscData = null (-1)");
            int posA = pos + 4;
            int posB = pos + 6;

            for (int variant = 0; variant < 2; variant++)
            {
                int p = variant == 0 ? posA : posB;
                string label = variant == 0 ? "noTag" : "withTag";

                if (p + 4 > end) continue;
                int v2 = BitConverter.ToInt32(d, p);
                Console.Write($"    [{label}] [0x{p-start:X3}] m_oGeomToElemMap = ");
                if (v2 == -1)
                {
                    Console.WriteLine($"null (-1)");
                    int p3 = p + 4;
                    for (int v2t = 0; v2t < 2; v2t++)
                    {
                        int pp = p3 + v2t * 2;
                        if (pp < end)
                            Console.WriteLine($"      [{label}+{(v2t==0?"noTag":"withTag")}] m_roomBounding @ 0x{pp-start:X3} = {d[pp]} ({(d[pp] != 0 ? "true" : "false")})");
                    }
                }
                else
                {
                    Console.WriteLine($"0x{(uint)v2:X8} (non-null)");
                }
            }
        }
        else if (pos + 4 <= end)
        {
            Console.WriteLine($"    [0x{pos-start:X3}] m_hostObjMiscData = 0x{BitConverter.ToUInt32(d, pos):X8} (non-null)");
        }
    }

    static void ScanForBipDoubles(byte[] d, int start, int end, int maxShow)
    {
        int shown = 0;
        for (int i = start; i + 4 <= end && shown < maxShow; i += 2)
        {
            int key = BitConverter.ToInt32(d, i);
            if (key >= -1 || key < -2_000_000) continue;

            int count = 0;
            for (int p = i; p + 4 <= end; p += 12)
            {
                int k = BitConverter.ToInt32(d, p);
                if (k == 0 || k == -1) break;
                if (k < -2_000_000 || k > 10_000_000) break;
                count++;
            }
            if (count < 3) continue;

            Console.WriteLine($"\n  --- BIP doubles at offset 0x{i - start:X3} ({count} entries) ---");
            for (int p = i, n = 0; p + 4 <= end && n < Math.Min(count, 8); p += 12, n++)
            {
                int k = BitConverter.ToInt32(d, p);
                double v = (p - 8 >= start) ? BitConverter.ToDouble(d, p - 8) : 0;
                Console.WriteLine($"  key={k,10} value={v:G6}");
            }
            shown++;
            i += count * 12;
        }
    }

    static void DumpGRep(byte[] d, int start, int end)
    {
        int len = end - start;
        if (len < 16) { Console.WriteLine("  (too short)"); return; }

        // GInfoBlob header
        int tag = BitConverter.ToInt32(d, start);
        int ctrlCmd = BitConverter.ToInt32(d, start + 4);
        int gStyleId = BitConverter.ToInt32(d, start + 8);
        uint flags = BitConverter.ToUInt32(d, start + 12);
        Console.WriteLine($"  Header: Tag={tag} CtrlCmd={ctrlCmd} GStyleElemId={gStyleId} Flags=0x{flags:X8}");

        if (start + 24 > end) return;
        int count1 = BitConverter.ToInt32(d, start + 16);
        int count2 = BitConverter.ToInt32(d, start + 20);
        Console.WriteLine($"  NodeCounts: {count1}, {count2}");

        int pos = start + 24;
        int nodeCount = 0;
        while (pos + 6 <= end && nodeCount < count1 + count2 + 20)
        {
            ushort nodeType = BitConverter.ToUInt16(d, pos);
            if (nodeType != 0x07DF && nodeType != 0x082C && nodeType != 0x06C8 && nodeType != 0x07B6
                && nodeType != 0x07E0 && nodeType != 0x0826 && nodeType != 0x0827)
                break;
            int nodeIdx = BitConverter.ToInt32(d, pos + 2);
            Console.WriteLine($"  Node[{nodeCount}]: type=0x{nodeType:X4} idx={nodeIdx}");
            pos += 6;
            nodeCount++;
            if (nodeCount > 50) break;
        }

        Console.WriteLine($"\n  --- Data after node table (offset 0x{pos - start:X3}) ---");
        int dumpLen = Math.Min(end - pos, 512);
        HexDump(d, pos, dumpLen);
        if (end - pos > 512)
            Console.WriteLine($"  ... ({end - pos - 512} more bytes)");

        var markerCounts = new Dictionary<ushort, int>();
        for (int i = start; i + 2 <= end; i += 2)
        {
            ushort m = BitConverter.ToUInt16(d, i);
            if (m == 0x082C || m == 0x06C8 || m == 0x07B6 || m == 0x07DF || m == 0x07E0)
            {
                if (!markerCounts.ContainsKey(m)) markerCounts[m] = 0;
                markerCounts[m]++;
            }
        }
        Console.WriteLine("\n  --- Marker scan (2-byte aligned) ---");
        foreach (var kv in markerCounts.OrderByDescending(x => x.Value))
            Console.WriteLine($"  0x{kv.Key:X4}: {kv.Value} occurrences");

        Console.WriteLine("\n  --- 0x06C8 (GPoint) first 10 markers ---");
        int gpShown = 0;
        for (int i = start; i + 2 <= end; i += 2)
        {
            if (BitConverter.ToUInt16(d, i) == 0x06C8)
            {
                Console.Write($"  offset 0x{i - start:X4}: ");
                int dLen = Math.Min(48, end - i - 2);
                for (int j = 0; j < dLen; j++) Console.Write($"{d[i + 2 + j]:X2} ");
                Console.WriteLine();
                if (i + 2 + 24 <= end)
                {
                    double px = BitConverter.ToDouble(d, i + 2);
                    double py = BitConverter.ToDouble(d, i + 10);
                    double pz = BitConverter.ToDouble(d, i + 18);
                    if (double.IsFinite(px) && double.IsFinite(py) && double.IsFinite(pz))
                        Console.WriteLine($"    -> point({px:F4}, {py:F4}, {pz:F4})");
                }
                if (++gpShown >= 10) { Console.WriteLine("  ..."); break; }
            }
        }

        Console.WriteLine("\n  --- 0x07B6 (GCurve) first 5 markers ---");
        int gcShown = 0;
        for (int i = start; i + 2 <= end; i += 2)
        {
            if (BitConverter.ToUInt16(d, i) == 0x07B6)
            {
                Console.Write($"  offset 0x{i - start:X4}: ");
                int dLen = Math.Min(80, end - i - 2);
                for (int j = 0; j < dLen; j++) Console.Write($"{d[i + 2 + j]:X2} ");
                Console.WriteLine();
                if (++gcShown >= 5) { Console.WriteLine("  ..."); break; }
            }
        }

        Console.WriteLine("\n  --- 0x082C (Geometry) markers ---");
        int shown = 0;
        for (int i = start; i + 50 <= end; i += 2)
        {
            if (BitConverter.ToUInt16(d, i) == 0x082C)
            {
                Console.Write($"  offset 0x{i - start:X4}: ");
                if (i + 2 + 48 <= end)
                {
                    var vals = new double[6];
                    bool allFin = true;
                    for (int k = 0; k < 6; k++)
                    {
                        vals[k] = BitConverter.ToDouble(d, i + 2 + k * 8);
                        if (!double.IsFinite(vals[k])) allFin = false;
                    }
                    if (allFin)
                        Console.WriteLine($"bbox? [{vals[0]:F4}, {vals[1]:F4}, {vals[2]:F4}] -> [{vals[3]:F4}, {vals[4]:F4}, {vals[5]:F4}]");
                    else
                        Console.WriteLine("(non-finite doubles)");
                }
                if (++shown > 10) { Console.WriteLine("  ..."); break; }
            }
        }

        // Scan for face planes: [env:32][orient:1][origin:24][xvec:24][yvec:24] = 105 bytes
        Console.WriteLine("\n  --- Face planes detected ---");
        int faceCount = 0;
        for (int i = start + 16; i + 105 <= end; i++)
        {
            byte orient = d[i + 32];
            if (orient > 1) continue;
            double minU = BitConverter.ToDouble(d, i);
            double minV = BitConverter.ToDouble(d, i + 8);
            double maxU = BitConverter.ToDouble(d, i + 16);
            double maxV = BitConverter.ToDouble(d, i + 24);
            if (!double.IsFinite(minU) || !double.IsFinite(minV) || !double.IsFinite(maxU) || !double.IsFinite(maxV)) continue;
            if (minU > maxU || minV > maxV) continue;
            double area = (maxU - minU) * (maxV - minV);
            if (area < 1e-12) continue;

            int planeOff = i + 33;
            if (planeOff + 72 > end) break;
            var v = new double[9];
            bool ok = true;
            for (int k = 0; k < 9; k++) { v[k] = BitConverter.ToDouble(d, planeOff + k * 8); if (!double.IsFinite(v[k])) { ok = false; break; } }
            if (!ok) continue;
            double xL = Math.Sqrt(v[3]*v[3]+v[4]*v[4]+v[5]*v[5]);
            double yL = Math.Sqrt(v[6]*v[6]+v[7]*v[7]+v[8]*v[8]);
            if (Math.Abs(xL-1)>0.01 || Math.Abs(yL-1)>0.01) continue;
            double dot = v[3]*v[6]+v[4]*v[7]+v[5]*v[8];
            if (Math.Abs(dot) > 0.01) continue;

            double nx = v[3]*v[7]-v[4]*v[6], ny = v[4]*v[8]-v[5]*v[7], nz = v[5]*v[6]-v[3]*v[8];
            Console.WriteLine($"  Face[{faceCount}] @ 0x{i-start:X4}: env=[{minU:F4},{minV:F4},{maxU:F4},{maxV:F4}] area={area:F3}sqft");
            Console.WriteLine($"    origin=[{v[0]:F4},{v[1]:F4},{v[2]:F4}] xvec=[{v[3]:F4},{v[4]:F4},{v[5]:F4}] yvec=[{v[6]:F4},{v[7]:F4},{v[8]:F4}] N=[{nx:F3},{ny:F3},{nz:F3}]");
            double c0x=v[0]+minU*v[3]+minV*v[6], c0y=v[1]+minU*v[4]+minV*v[7], c0z=v[2]+minU*v[5]+minV*v[8];
            double c1x=v[0]+maxU*v[3]+minV*v[6], c1y=v[1]+maxU*v[4]+minV*v[7], c1z=v[2]+maxU*v[5]+minV*v[8];
            double c2x=v[0]+maxU*v[3]+maxV*v[6], c2y=v[1]+maxU*v[4]+maxV*v[7], c2z=v[2]+maxU*v[5]+maxV*v[8];
            double c3x=v[0]+minU*v[3]+maxV*v[6], c3y=v[1]+minU*v[4]+maxV*v[7], c3z=v[2]+minU*v[5]+maxV*v[8];
            Console.WriteLine($"    corners: ({c0x:F4},{c0y:F4},{c0z:F4}) ({c1x:F4},{c1y:F4},{c1z:F4}) ({c2x:F4},{c2y:F4},{c2z:F4}) ({c3x:F4},{c3y:F4},{c3z:F4})");

            int preStart = Math.Max(start, i - 32);
            Console.Write($"    32 bytes before: ");
            for (int j = preStart; j < i; j++) Console.Write($"{d[j]:X2} ");
            Console.WriteLine();

            int postStart = planeOff + 72;
            Console.Write($"    32 bytes after:  ");
            for (int j = postStart; j < Math.Min(end, postStart + 32); j++) Console.Write($"{d[j]:X2} ");
            Console.WriteLine();

            faceCount++;
            i = planeOff + 72 - 1;
            if (faceCount > 20) { Console.WriteLine("  ..."); break; }
        }
        Console.WriteLine($"  Total faces: {faceCount}");

        double[]? scanBbox = null;
        for (int i = start; i + 50 <= end; i += 2)
        {
            if (d[i] == 0x2C && d[i+1] == 0x08 && i + 2 + 48 <= end)
            {
                var bv = new double[6];
                bool ok2 = true;
                for (int k = 0; k < 6; k++) { bv[k] = BitConverter.ToDouble(d, i+2+k*8); if (!double.IsFinite(bv[k])) ok2 = false; }
                if (ok2 && bv[0] <= bv[3] && bv[1] <= bv[4] && bv[2] <= bv[5]
                    && (bv[3]-bv[0]+bv[4]-bv[1]+bv[5]-bv[2]) > 0.01)
                { scanBbox = bv; break; }
            }
        }

        // Scan for 3D point coordinates: consecutive double triples in the bbox range
        if (scanBbox != null)
        {
            double[] bb = scanBbox;
            double margin = 1.0; // allow slight outside bbox
            Console.WriteLine($"\n  --- Scanning for 3D points within bbox +/-{margin}ft ---");
            int ptCount = 0;
            for (int i = start; i + 24 <= end; i += 8)
            {
                double x = BitConverter.ToDouble(d, i);
                double y = BitConverter.ToDouble(d, i + 8);
                double z = BitConverter.ToDouble(d, i + 16);
                if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)) continue;
                if (x < bb[0] - margin || x > bb[3] + margin) continue;
                if (y < bb[1] - margin || y > bb[4] + margin) continue;
                if (z < bb[2] - margin || z > bb[5] + margin) continue;
                if (Math.Abs(x) < 1e-10 && Math.Abs(y) < 1e-10 && Math.Abs(z) < 1e-10) continue;

                if (ptCount < 30)
                    Console.WriteLine($"  pt[{ptCount}] @ 0x{i-start:X4}: ({x:F4}, {y:F4}, {z:F4})");
                ptCount++;
            }
            Console.WriteLine($"  Total candidate points: {ptCount}");
        }
    }

    static void ScanForWallAnchor(byte[] d, int start, int end)
    {
        for (int i = start; i + 24 <= end; i += 2)
        {
            int v0 = BitConverter.ToInt32(d, i);
            int v1 = BitConverter.ToInt32(d, i + 4);
            int v2 = BitConverter.ToInt32(d, i + 8);
            int v3 = BitConverter.ToInt32(d, i + 12);
            int v4 = BitConverter.ToInt32(d, i + 16);
            int v5 = BitConverter.ToInt32(d, i + 20);

            if (v0 > 0 && v0 < 10_000_000 && v5 == -1)
            {
                bool anal1 = v2 == -9 && v3 >= -100 && v3 <= 0 && v4 >= -100 && v4 <= 0;
                bool anal2 = v2 == -1 && v3 == -1 && v4 == -1;
                if (anal1 || anal2)
                {
                    Console.WriteLine($"\n  --- Wall anchor at offset 0x{i - (end - (end - start)):X3} ---");
                    Console.WriteLine($"  WallAttrId  = {v0}");
                    Console.WriteLine($"  UpToLevelId = {v1}");
                    Console.WriteLine($"  AnalSurf    = {v2}, AnalTop = {v3}, AnalBot = {v4}");
                    Console.WriteLine($"  ContFoot    = {v5}");
                    if (i + 50 <= end)
                    {
                        Console.WriteLine($"  +36 StructUsage  = {BitConverter.ToInt32(d, i + 36)}");
                        Console.WriteLine($"  +40 KeyRef       = {BitConverter.ToInt32(d, i + 40)}");
                        Console.WriteLine($"  +44 CrossSection = {BitConverter.ToInt32(d, i + 44)}");
                        Console.WriteLine($"  +48 IsFlipped    = {d[i + 48]}");
                        Console.WriteLine($"  +49 IsStructSig  = {d[i + 49]}");
                    }
                    return;
                }
            }
        }
    }
}
