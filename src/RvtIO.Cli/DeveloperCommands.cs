using System.Text.Json;
using System.Text.Json.Serialization;
using RvtIO;
using RvtIO.Compact;
using RvtIO.BuiltIns;
using RvtIO.Ifc;
using RvtIO.Model;

internal static partial class Program
{
    private static int ChunkDump(string rvtPath, string chunkDumpArg)
    {
        var parts = chunkDumpArg.Split(':');
        int chunkIdx = int.Parse(parts[0]);
        int startOff = Convert.ToInt32(parts[1], 16);
        int dumpLen = parts.Length > 2 ? Convert.ToInt32(parts[2], 16) : 0x200;

        using var rootFsCd = OpenMcdf.RootStorage.OpenRead(rvtPath);
        var psCd = rootFsCd.OpenStorage("Partitions");
        var peCd = psCd.EnumerateEntries().Where(e => e.Type == OpenMcdf.EntryType.Stream).OrderByDescending(e => e.Length).ToList();
        using var pstrCd = psCd.OpenStream(peCd[0].Name);
        var rawCd = new byte[pstrCd.Length];
        pstrCd.ReadExactly(rawCd);
        var dcCd = new RvtIO.Parsing.ChunkDecompressor();
        var cksCd = dcCd.DecompressAll(rawCd);
        Console.Error.WriteLine($"chunk[{chunkIdx}] size={cksCd[chunkIdx].Length} dumping {dumpLen} bytes from 0x{startOff:X}");
        var chunk = cksCd[chunkIdx];
        int dEnd = Math.Min(chunk.Length, startOff + dumpLen);
        for (int row = startOff; row < dEnd; row += 16)
        {
            Console.Write($"  {row:X6}: ");
            var hex = new System.Text.StringBuilder();
            var asc = new System.Text.StringBuilder();
            for (int col = 0; col < 16 && row + col < dEnd; col++)
            {
                byte b = chunk[row + col];
                hex.Append($"{b:X2} ");
                asc.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
                if (col == 7) hex.Append(" ");
            }
            Console.Write(hex.ToString().PadRight(50));
            Console.WriteLine(" " + asc);
        }
        return 0;
    }

    // --grep-inspect <id1,id2,...>: decode only the requested serialized GRep blocks.
    // This avoids building the complete document when investigating geometry grammar.
    private static int GrepInspect(string rvtPath, string grepInspectIds)
    {
        var targetIds = new HashSet<uint>(grepInspectIds.Split(',')
            .Select(value => uint.Parse(value.Trim())));
        using var root = OpenMcdf.RootStorage.OpenRead(rvtPath);
        var partitions = root.OpenStorage("Partitions");
        var partitionEntry = partitions.EnumerateEntries()
            .Where(entry => entry.Type == OpenMcdf.EntryType.Stream)
            .OrderByDescending(entry => entry.Length)
            .First();
        using var partitionStream = partitions.OpenStream(partitionEntry.Name);
        var compressed = new byte[partitionStream.Length];
        partitionStream.ReadExactly(compressed);
        var decompressor = new RvtIO.Parsing.ChunkDecompressor();
        var chunks = decompressor.DecompressAll(compressed);
        var found = new Dictionary<uint, int>();
        RvtIO.Parsing.TfStreamWalker.ScanBlocks(chunks, targetIds,
            (segment, blockStart, dataSize, classId, elementId) =>
            {
                if (classId is not (0x07D7 or 0x07D6)) return;
                var geometry = RvtIO.Parsing.GRepParserV2.Parse(segment, blockStart, dataSize);
                found.TryGetValue(elementId, out int blockIndex);
                found[elementId] = blockIndex + 1;
                Console.WriteLine($"GRep {elementId} block={blockIndex + 1} class={classId} bytes={dataSize}");
                if (geometry == null)
                {
                    Console.WriteLine("  decoded=null");
                    return;
                }
                Console.WriteLine($"  faces={geometry.Faces.Count} solids={geometry.Solids.Count} "
                    + $"closed={geometry.Solids.Count(solid => solid.IsClosed)} open={geometry.IsOpenSurface}");
                Console.WriteLine($"  face-vertices=[{string.Join(',', geometry.Faces
                    .GroupBy(face => face.Vertices?.Count ?? 0)
                    .OrderBy(group => group.Key)
                    .Select(group => $"{group.Key}:{group.Count()}"))}]");
                int decodedVertexCount = geometry.Faces.Sum(face => face.Vertices?.Count ?? 0);
                if (geometry.Faces.Count <= 64 && decodedVertexCount <= 256)
                    for (int faceIndex = 0; faceIndex < geometry.Faces.Count; faceIndex++)
                        Console.WriteLine($"  face {faceIndex} vertices=[{string.Join(';',
                            geometry.Faces[faceIndex].Vertices?.Select(point =>
                                $"({string.Join(',', point.Select(value => value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)))})")
                            ?? [])}]");
                for (int solidIndex = 0; solidIndex < geometry.Solids.Count; solidIndex++)
                {
                    var solid = geometry.Solids[solidIndex];
                    Console.WriteLine($"  solid {solidIndex} faces={solid.FaceIndices.Count} "
                        + $"closed={solid.IsClosed} indices=[{string.Join(',', solid.FaceIndices)}] "
                        + $"face-vertices=[{string.Join(',', solid.FaceIndices
                            .Where(index => index >= 0 && index < geometry.Faces.Count)
                            .Select(index => geometry.Faces[index].Vertices?.Count ?? 0)
                            .GroupBy(count => count)
                            .OrderBy(group => group.Key)
                            .Select(group => $"{group.Key}:{group.Count()}"))}]");
                }
                Console.WriteLine($"  bbox=[{string.Join(',', geometry.BoundingBox ?? [])}]");
                foreach (var instance in geometry.Instances)
                    Console.WriteLine($"  instance symbol={instance.SymbolElemId} "
                        + $"transform=[{string.Join(',', instance.Transform)}]");
            });
        foreach (uint elementId in targetIds.OrderBy(value => value))
            if (!found.ContainsKey(elementId)) Console.WriteLine($"GRep {elementId}: NOT FOUND");
        return 0;
    }

    private static int BlockScan(string rvtPath, string blockScanIds)
    {
        var targetEids2 = new HashSet<uint>(blockScanIds.Split(',').Select(s => uint.Parse(s.Trim())));
        using var rootFs4 = OpenMcdf.RootStorage.OpenRead(rvtPath);
        var ps2 = rootFs4.OpenStorage("Partitions");
        var pe2 = ps2.EnumerateEntries().Where(e => e.Type == OpenMcdf.EntryType.Stream).OrderByDescending(e => e.Length).ToList();
        using var pstr2 = ps2.OpenStream(pe2[0].Name);
        var rawD2 = new byte[pstr2.Length];
        pstr2.ReadExactly(rawD2);
        var dc3 = new RvtIO.Parsing.ChunkDecompressor();
        var cks2 = dc3.DecompressAll(rawD2);
        Console.Error.WriteLine($"Block-scanning {cks2.Count} chunks for eids {string.Join(",", targetEids2)}...");

        for (int ci = 0; ci < cks2.Count; ci++)
        {
            var chunk = cks2[ci];
            // Scan for the element ID at every byte boundary (blocks are sometimes not 4-byte aligned)
            for (int pos = 0; pos + 4 <= chunk.Length; pos += 1)
            {
                uint eid = BitConverter.ToUInt32(chunk, pos);
                if (!targetEids2.Contains(eid)) continue;

                if (pos + 12 <= chunk.Length)
                {
                    uint rSize = BitConverter.ToUInt32(chunk, pos + 4);
                    ushort magic = BitConverter.ToUInt16(chunk, pos + 8);
                    if ((magic == 0x053C || magic == 0x04F8) && rSize > 0 && rSize < 500000 && pos + 12 + (int)rSize <= chunk.Length)
                    {
                        Console.WriteLine($"RECORD: eid={eid} chunk[{ci}] pos=0x{pos:X} bodySize={rSize} magic=0x{magic:X4}");
                        continue;
                    }
                }
                // Check if this is a block header: [eid:4][hash:4][dataSize:4][classIdx:4]
                if (pos + 16 <= chunk.Length)
                {
                    int dSize = BitConverter.ToInt32(chunk, pos + 8);
                    uint classIdx = BitConverter.ToUInt32(chunk, pos + 12);
                    if (dSize > 0 && dSize < 1_000_000 && pos + 16 + dSize <= chunk.Length)
                    {
                        bool isFFFF = (classIdx >> 16) == 0xFFFF;
                        bool isGRep = (classIdx & 0xFFFF) is 0x07D7 or 0x07D6;
                        bool is0702 = (classIdx & 0xFFFF) == 0x0702;
                        Console.Write($"BLOCK: eid={eid} chunk[{ci}] pos=0x{pos:X} dataSize={dSize} classIdx=0x{classIdx:X8}");
                        if (isFFFF) Console.Write(" [0xFFFF]");
                        if (isGRep) Console.Write(" [GRep]");
                        if (is0702) Console.Write(" [0x702]");
                        Console.WriteLine();
                        int dataStart = pos + 16;
                        int dataEnd = dataStart + dSize;
                        int idCount = 0;
                        for (int j = dataStart; j + 4 <= dataEnd && idCount < 15; j += 2)
                        {
                            if (BitConverter.ToUInt32(chunk, j) != eid) continue;
                            int off = j - dataStart;
                            Console.Write($"  EID@0x{off:X}: ");
                            for (int h = 0; h < Math.Min(80, dataEnd - j); h++) Console.Write($"{chunk[j + h]:X2} ");
                            Console.WriteLine();
                            idCount++;
                        }
                        for (int j = dataStart; j + 6 <= dataEnd; j++)
                        {
                            int sLen = BitConverter.ToInt32(chunk, j);
                            if (sLen < 2 || sLen > 500 || j + 4 + sLen * 2 > dataEnd) continue;
                            bool valid = true; int printable = 0;
                            for (int k = 0; k < sLen && valid; k++)
                            {
                                byte lo = chunk[j + 4 + k * 2], hi = chunk[j + 4 + k * 2 + 1];
                                if (hi != 0x00 && hi != 0x04 && hi != 0xF7 && hi != 0x1B) { valid = false; break; }
                                if ((hi == 0x00 && lo >= 0x20 && lo <= 0x7E) || hi == 0x04 || hi == 0xF7 || hi == 0x1B) printable++;
                            }
                            if (!valid || printable < sLen * 0.5) continue;
                            var sb = new System.Text.StringBuilder();
                            for (int k = 0; k < sLen; k++)
                            {
                                byte lo = chunk[j + 4 + k * 2], hi = chunk[j + 4 + k * 2 + 1];
                                if (hi == 0x04) sb.Append((char)(0x0400 | lo));
                                else if (hi == 0xF7 || hi == 0x1B) sb.Append((char)lo);
                                else sb.Append((char)lo);
                            }
                            if (sb.Length >= 2)
                                Console.WriteLine($"  STRING @ body+0x{j - dataStart:X3}: [{sLen}] \"{sb}\"");
                        }
                    }
                }
            }
        }
        return 0;
    }

    private static int HexSearch(string rvtPath, string hexSearch)
    {
        byte[] needle = Convert.FromHexString(hexSearch);
        using var rootHs = OpenMcdf.RootStorage.OpenRead(rvtPath);
        var psHs = rootHs.OpenStorage("Partitions");
        var peHs = psHs.EnumerateEntries().Where(e => e.Type == OpenMcdf.EntryType.Stream).OrderByDescending(e => e.Length).ToList();
        using var pstrHs = psHs.OpenStream(peHs[0].Name);
        var rawHs = new byte[pstrHs.Length];
        pstrHs.ReadExactly(rawHs);
        var chunksHs = new RvtIO.Parsing.ChunkDecompressor().DecompressAll(rawHs);
        for (int ci = 0; ci < chunksHs.Count; ci++)
        {
            var chunk = chunksHs[ci];
            for (int at = chunk.AsSpan().IndexOf(needle); at >= 0;)
            {
                int from = Math.Max(0, at - 32), to = Math.Min(chunk.Length, at + needle.Length + 32);
                Console.WriteLine($"chunk[{ci}] @0x{at:X}: {Convert.ToHexString(chunk, from, to - from)} (match at +{at - from})");
                int next = chunk.AsSpan(at + 1).IndexOf(needle);
                at = next < 0 ? -1 : at + 1 + next;
            }
        }
        return 0;
    }

    private static int RecDump(string rvtPath, string recDumpIds)
    {
        var targetEids = new HashSet<uint>(recDumpIds.Split(',').Select(s => uint.Parse(s.Trim())));
        using var rootFs2 = OpenMcdf.RootStorage.OpenRead(rvtPath);
        var ps = rootFs2.OpenStorage("Partitions");
        var pe = ps.EnumerateEntries().Where(e => e.Type == OpenMcdf.EntryType.Stream).OrderByDescending(e => e.Length).ToList();
        using var pstr = ps.OpenStream(pe[0].Name);
        var rawD = new byte[pstr.Length];
        pstr.ReadExactly(rawD);
        var dc = new RvtIO.Parsing.ChunkDecompressor();
        var cks = dc.DecompressAll(rawD);
        Console.Error.WriteLine($"Searching {cks.Count} chunks for records of {string.Join(",", targetEids)}...");
        foreach (var (ci, chunk) in cks.Select((c, i) => (i, c)))
        {
            int pos2 = 0;
            while (pos2 + 12 <= chunk.Length)
            {
                uint eid = BitConverter.ToUInt32(chunk, pos2);
                uint bodySize = BitConverter.ToUInt32(chunk, pos2 + 4);
                ushort magic = BitConverter.ToUInt16(chunk, pos2 + 8);
                if ((magic == 0x053C || magic == 0x04F8) && eid > 0 && eid < 10_000_000 && bodySize > 0 && bodySize < 500_000
                    && pos2 + 12 + (int)bodySize <= chunk.Length)
                {
                    if (targetEids.Contains(eid))
                    {
                        Console.WriteLine($"\n=== RECORD eid={eid} chunk[{ci}] bodySize={bodySize} magic=0x{magic:X4} ===");
                        int dLen = Math.Min((int)bodySize, 1024);
                        for (int r = 0; r < dLen; r += 16)
                        {
                            Console.Write($"  {r:X4}: ");
                            for (int c2 = 0; c2 < 16 && r + c2 < dLen; c2++)
                            {
                                Console.Write($"{chunk[pos2 + 12 + r + c2]:X2} ");
                                if (c2 == 7) Console.Write(" ");
                            }
                            Console.Write(" ");
                            for (int c2 = 0; c2 < 16 && r + c2 < dLen; c2++)
                            {
                                byte b = chunk[pos2 + 12 + r + c2];
                                Console.Write(b >= 0x20 && b < 0x7F ? (char)b : '.');
                            }
                            Console.WriteLine();
                        }
                        if (bodySize > 1024) Console.WriteLine($"  ... ({bodySize - 1024} more bytes)");
                        Console.WriteLine("\n  --- Strings in body ---");
                        int bodyStart = pos2 + 12;
                        int bodyEnd = bodyStart + (int)bodySize;
                        for (int j = bodyStart; j + 6 <= bodyEnd; j++)
                        {
                            int sLen = BitConverter.ToInt32(chunk, j);
                            if (sLen < 2 || sLen > 500 || j + 4 + sLen * 2 > bodyEnd) continue;
                            bool valid = true;
                            int printable = 0;
                            for (int k = 0; k < sLen && valid; k++)
                            {
                                byte lo = chunk[j + 4 + k * 2], hi = chunk[j + 4 + k * 2 + 1];
                                if (hi != 0x00 && hi != 0x04) { valid = false; break; }
                                if ((hi == 0x00 && lo >= 0x20 && lo <= 0x7E) || hi == 0x04) printable++;
                            }
                            if (!valid || printable < sLen * 0.6) continue;
                            var sb = new System.Text.StringBuilder();
                            for (int k = 0; k < sLen; k++)
                            {
                                byte lo = chunk[j + 4 + k * 2], hi = chunk[j + 4 + k * 2 + 1];
                                sb.Append(hi == 0x04 ? (char)(0x0400 | lo) : (char)lo);
                            }
                            string str = sb.ToString().Trim();
                            if (str.Length >= 2)
                                Console.WriteLine($"    offset 0x{j - bodyStart:X3}: [{sLen}] \"{str}\"");
                        }
                    }
                    pos2 += 12 + (int)bodySize;
                    continue;
                }
                pos2++;
            }
        }
        return 0;
    }

    // --block-dump 0xCLASSIDX : dump hex of blocks with given classIdx
    private static int BlockDump(string rvtPath, string blockDumpArg)
    {
        uint targetClass = Convert.ToUInt32(blockDumpArg, 16);
        var readerDmp = new RvtFileReader(new RvtReaderOptions { DiagnosticOutput = Console.Error });
        var (_, stateDmp) = readerDmp.ReadWithState(rvtPath);

        int found = 0;
        foreach (var chunk in stateDmp.NonRecordChunks.Concat(stateDmp.RecordChunks))
        {
            int pos = 0;
            while (pos + 16 <= chunk.Length)
            {
                uint eid = BitConverter.ToUInt32(chunk, pos);
                if (pos + 12 <= chunk.Length)
                {
                    uint rBodySize = BitConverter.ToUInt32(chunk, pos + 4);
                    ushort rMagic = BitConverter.ToUInt16(chunk, pos + 8);
                    if ((rMagic == 0x053C || rMagic == 0x04F8) && eid > 0 && eid < 10_000_000
                        && rBodySize > 0 && rBodySize < 500_000 && pos + 12 + (int)rBodySize <= chunk.Length)
                    { pos += 12 + (int)rBodySize; continue; }
                }
                uint dataSize = BitConverter.ToUInt32(chunk, pos + 8);
                if (dataSize > 1_000_000) { pos++; continue; }
                if (eid == 0) { pos += 4; continue; }
                if (dataSize == 0) { pos++; continue; }
                if (pos + 16 + (int)dataSize > chunk.Length) break;

                uint classIdx = BitConverter.ToUInt32(chunk, pos + 12);
                if ((classIdx & 0xFFFF) == targetClass && eid > 0 && eid < 10_000_000 && found < 5)
                {
                    found++;
                    int dStart = pos + 16;
                    uint typeId = stateDmp.ElemTable.TryGetValue(eid, out var tid) ? tid : 0;
                    Console.WriteLine($"\n=== Block #{found}: eid={eid} typeId={typeId} classIdx=0x{classIdx:X8} dataSize={dataSize} ===");
                    int dumpLen = Math.Min((int)dataSize, 512);
                    for (int row = 0; row < dumpLen; row += 16)
                    {
                        var hexPart = new System.Text.StringBuilder();
                        var ascPart = new System.Text.StringBuilder();
                        for (int col = 0; col < 16 && row + col < dumpLen; col++)
                        {
                            byte b = chunk[dStart + row + col];
                            hexPart.Append($"{b:X2} ");
                            ascPart.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
                        }
                        Console.WriteLine($"  {row:X4}: {hexPart,-49} {ascPart}");
                    }
                    if (dataSize > 512) Console.WriteLine($"  ... ({dataSize - 512} more bytes)");

                    var strings = new List<string>();
                    for (int j = dStart; j + 6 <= dStart + (int)dataSize; j += 2)
                    {
                        if (j + 4 > dStart + (int)dataSize) break;
                        uint slen = BitConverter.ToUInt32(chunk, j);
                        if (slen > 0 && slen < 500 && j + 4 + slen * 2 <= dStart + (int)dataSize)
                        {
                            bool valid = true;
                            for (int k = 0; k < (int)slen && valid; k++)
                            {
                                byte hi = chunk[j + 4 + k * 2 + 1];
                                byte lo = chunk[j + 4 + k * 2];
                                if (hi > 0x04 && hi != 0xF7) valid = false;
                                if (hi == 0 && lo < 0x20 && lo != 0x0A && lo != 0x0D && lo != 0x09) valid = false;
                            }
                            if (valid)
                            {
                                string s = System.Text.Encoding.Unicode.GetString(chunk, j + 4, (int)slen * 2);
                                if (s.Length >= 3) strings.Add($"  @{j - dStart}: \"{s}\"");
                            }
                        }
                    }
                    if (strings.Count > 0)
                    {
                        Console.WriteLine("  Strings:");
                        foreach (var s in strings.Take(20)) Console.WriteLine(s);
                    }
                }
                pos += 16 + (int)dataSize;
            }
        }
        Console.Error.WriteLine($"Found {found} blocks with classIdx 0x{targetClass:X4}");
        return 0;
    }

    // --decode-blocks : structurally decode top unknown block types (0x0FBD, 0x0725, 0x091F)
    private static int DecodeBlocks(string rvtPath)
    {
        var readerDb = new RvtFileReader(new RvtReaderOptions { DiagnosticOutput = Console.Error });
        var (_, stateDb) = readerDb.ReadWithState(rvtPath);

        var blocksByClass = new Dictionary<uint, List<(uint eid, byte[] data)>>();
        foreach (var chunk in stateDb.NonRecordChunks.Concat(stateDb.RecordChunks))
        {
            int pos = 0;
            while (pos + 16 <= chunk.Length)
            {
                uint eid = BitConverter.ToUInt32(chunk, pos);
                if (pos + 12 <= chunk.Length)
                {
                    uint rBodySize = BitConverter.ToUInt32(chunk, pos + 4);
                    ushort rMagic = BitConverter.ToUInt16(chunk, pos + 8);
                    if ((rMagic == 0x053C || rMagic == 0x04F8) && eid > 0 && eid < 10_000_000
                        && rBodySize > 0 && rBodySize < 500_000 && pos + 12 + (int)rBodySize <= chunk.Length)
                    { pos += 12 + (int)rBodySize; continue; }
                }
                uint dataSize = BitConverter.ToUInt32(chunk, pos + 8);
                if (dataSize > 1_000_000) { pos++; continue; }
                if (eid == 0) { pos += 4; continue; }
                if (dataSize == 0) { pos++; continue; }
                if (pos + 16 + (int)dataSize > chunk.Length) break;
                uint classIdx = BitConverter.ToUInt32(chunk, pos + 12);
                uint classKey = classIdx & 0xFFFF;
                if (eid > 0 && eid < 10_000_000)
                {
                    if (!blocksByClass.TryGetValue(classKey, out var list))
                        blocksByClass[classKey] = list = new List<(uint, byte[])>();
                    var data = new byte[(int)dataSize];
                    Array.Copy(chunk, pos + 16, data, 0, (int)dataSize);
                    list.Add((eid, data));
                }
                pos += 16 + (int)dataSize;
            }
        }

        // Decode 0x0FBD: ForgeTypeId parameter specification registry
        if (blocksByClass.TryGetValue(0x0FBD, out var fbdBlocks))
        {
            Console.WriteLine($"\n===== 0x0FBD: ForgeTypeId Spec Registry ({fbdBlocks.Count} blocks) =====");
            var specEntries = new List<(string spec, string symbol, string unit, double factor)>();
            foreach (var (eid, data) in fbdBlocks.Take(1))
            {
                Console.WriteLine($"  Block eid={eid}, size={data.Length}");
                var allStrings = new List<(int offset, string value)>();
                for (int j = 0; j + 6 <= data.Length; j++)
                {
                    uint sLen = BitConverter.ToUInt32(data, j);
                    if (sLen < 3 || sLen > 500 || j + 4 + (int)sLen * 2 > data.Length) continue;
                    bool valid = true;
                    for (int k = 0; k < (int)sLen && valid; k++)
                    {
                        byte lo = data[j + 4 + k * 2], hi = data[j + 4 + k * 2 + 1];
                        if (hi > 0x04 && hi != 0xF7) valid = false;
                        if (hi == 0 && lo < 0x20 && lo != 0x09) valid = false;
                    }
                    if (!valid) continue;
                    string s = System.Text.Encoding.Unicode.GetString(data, j + 4, (int)sLen * 2);
                    if (s.Length >= 3 && s.Contains('.'))
                    {
                        allStrings.Add((j, s));
                        j += 3 + (int)sLen * 2;
                    }
                }

                Console.WriteLine($"  Found {allStrings.Count} ForgeTypeId strings");
                // Group into triples: spec, symbol, unit
                for (int i = 0; i < allStrings.Count - 2; i += 3)
                {
                    var (off0, spec) = allStrings[i];
                    var (off1, symbol) = allStrings[i + 1];
                    var (off2, unit) = allStrings[i + 2];

                    if (!spec.StartsWith("autodesk.spec.") && !spec.StartsWith("autodesk.parameter.") && !spec.StartsWith("autodesk."))
                    { Console.WriteLine($"  WARN: unexpected spec at @{off0}: {spec}"); continue; }
                    if (spec.Contains("unit.symbol:") || spec.Contains("unit.unit:"))
                    { i -= 2; continue; }

                    double factor = 0;
                    int afterUnit = off2 + 4 + unit.Length * 2;
                    if (afterUnit + 8 <= data.Length)
                        factor = BitConverter.ToDouble(data, afterUnit);

                    specEntries.Add((spec, symbol, unit, factor));
                }
            }

            Console.WriteLine($"\n  Total entries: {specEntries.Count}");
            Console.WriteLine($"  {"Spec",-55} {"Symbol",-35} {"Unit",-40} Factor");
            Console.WriteLine($"  {new string('-', 55)} {new string('-', 35)} {new string('-', 40)} ------");
            foreach (var (spec, symbol, unit, factor) in specEntries)
                Console.WriteLine($"  {spec,-55} {symbol,-35} {unit,-40} {factor:G6}");

            int sameCount = 0;
            foreach (var (eid, data) in fbdBlocks.Skip(1).Take(5))
            {
                for (int j = 0; j + 100 <= data.Length; j++)
                {
                    uint sLen = BitConverter.ToUInt32(data, j);
                    if (sLen >= 3 && sLen < 500 && j + 4 + (int)sLen * 2 <= data.Length)
                    {
                        bool valid = true;
                        for (int k = 0; k < (int)sLen && valid; k++)
                        {
                            byte hi = data[j + 4 + k * 2 + 1];
                            if (hi > 0x04) valid = false;
                        }
                        if (valid)
                        {
                            string s = System.Text.Encoding.Unicode.GetString(data, j + 4, (int)sLen * 2);
                            if (s.StartsWith("autodesk.spec.")) { sameCount++; break; }
                        }
                    }
                }
            }
            Console.WriteLine($"\n  Cross-block consistency: {sameCount}/{Math.Min(5, fbdBlocks.Count - 1)} blocks start with same spec pattern");
        }

        // Decode 0x0725: Entry-list blocks (family type parameter defs)
        if (blocksByClass.TryGetValue(0x0725, out var h25Blocks))
        {
            Console.WriteLine($"\n===== 0x0725: Family Type Parameter Definitions ({h25Blocks.Count} blocks) =====");
            int nameFoundCount = 0;
            var nameByEid = new Dictionary<uint, string>();
            foreach (var (eid, d) in h25Blocks)
            {
                // The first non-autodesk string longer than 2 chars is the element name
                for (int j = 400; j + 6 <= d.Length; j++) // skip Tf header region
                {
                    uint sLen = BitConverter.ToUInt32(d, j);
                    if (sLen < 2 || sLen > 200 || j + 4 + (int)sLen * 2 > d.Length) continue;
                    bool valid = true;
                    for (int k = 0; k < (int)sLen && valid; k++)
                    {
                        byte hi = d[j + 4 + k * 2 + 1];
                        if (hi > 0x04 && hi != 0xF7 && hi != 0x20) valid = false;
                        byte lo = d[j + 4 + k * 2];
                        if (hi == 0 && lo < 0x20) valid = false;
                    }
                    if (!valid) continue;
                    var s = System.Text.Encoding.Unicode.GetString(d, j + 4, (int)sLen * 2);
                    if (s.StartsWith("autodesk.")) continue;
                    if (s.Length < 2) continue;
                    nameByEid[eid] = s;
                    nameFoundCount++;
                    break;
                }
            }
            Console.WriteLine($"\n  Names extracted from 0x0725: {nameFoundCount}/{h25Blocks.Count} blocks");
            foreach (var (eid, name) in nameByEid.Take(10))
                Console.WriteLine($"    eid={eid}: \"{name}\"");
            foreach (var (eid, data) in h25Blocks.Take(3))
            {
                Console.WriteLine($"\n  Block eid={eid}, size={data.Length}");
                var tags = new List<(int offset, ushort tag)>();
                for (int j = 0; j + 2 <= data.Length; j += 2)
                {
                    ushort t = BitConverter.ToUInt16(data, j);
                    if (t == 0x0826 || t == 0x0827 || t == 0x028D || t == 0x0B47)
                        tags.Add((j, t));
                }
                Console.Write($"    Tags:");
                foreach (var (off, tag) in tags.Take(10))
                    Console.Write($" 0x{tag:X4}@{off}");
                Console.WriteLine();

                // Pattern: [count:4][type:4][elemRef:4 * count]
                for (int j = 0x70; j + 8 <= Math.Min(data.Length, 0x100); j += 2)
                {
                    uint cnt = BitConverter.ToUInt32(data, j);
                    if (cnt >= 3 && cnt <= 100 && j + 4 + (int)cnt * 4 <= data.Length)
                    {
                        uint typeFlag = BitConverter.ToUInt32(data, j + 4);
                        if (typeFlag > 10) continue;
                        uint first = BitConverter.ToUInt32(data, j + 8);
                        uint second = cnt > 1 ? BitConverter.ToUInt32(data, j + 12) : 0;
                        if (first > 100000 && first < 10_000_000 && (cnt <= 1 || (second > 100000 && second - first < 100)))
                        {
                            Console.Write($"    ElemRefList @{j}: count={cnt} type={typeFlag} refs=[");
                            for (int k = 0; k < Math.Min((int)cnt, 10); k++)
                            {
                                uint r = BitConverter.ToUInt32(data, j + 8 + k * 4);
                                Console.Write($"{r}");
                                if (k < Math.Min((int)cnt, 10) - 1) Console.Write(",");
                            }
                            if (cnt > 10) Console.Write("...");
                            Console.WriteLine("]");
                            break;
                        }
                    }
                }

                // Repeating sub-entries: look for [elemRef:4][FFFFFFFF:4][seqNum:4][FFFFFFFF:4]
                int subEntryCount = 0;
                var subEntries = new List<(uint elemRef, int seqNum, int offset)>();
                for (int j = 0x100; j + 32 <= data.Length; j += 2)
                {
                    uint eRef = BitConverter.ToUInt32(data, j);
                    if (eRef < 100000 || eRef > 10_000_000) continue;
                    if (BitConverter.ToUInt32(data, j + 4) != 0xFFFFFFFF) continue;
                    int seq = BitConverter.ToInt32(data, j + 8);
                    if (seq < 0 || seq > 200) continue;
                    subEntries.Add((eRef, seq, j));
                    subEntryCount++;
                }
                if (subEntries.Count > 0)
                {
                    Console.Write($"    SubEntries ({subEntries.Count}): ");
                    foreach (var (eRef, seq, off) in subEntries.Take(5))
                        Console.Write($"[eid={eRef},seq={seq}@{off}] ");
                    if (subEntries.Count > 5) Console.Write("...");
                    Console.WriteLine();
                }

                var forgeStrings = new List<string>();
                for (int j = 0; j + 6 <= data.Length; j++)
                {
                    uint sLen = BitConverter.ToUInt32(data, j);
                    if (sLen < 10 || sLen > 200 || j + 4 + (int)sLen * 2 > data.Length) continue;
                    bool valid = true;
                    for (int k = 0; k < (int)sLen && valid; k++)
                    {
                        byte hi = data[j + 4 + k * 2 + 1];
                        if (hi > 0x04) valid = false;
                    }
                    if (!valid) continue;
                    string s = System.Text.Encoding.Unicode.GetString(data, j + 4, (int)sLen * 2);
                    if (s.StartsWith("autodesk."))
                    {
                        forgeStrings.Add(s);
                        j += 3 + (int)sLen * 2;
                    }
                }
                if (forgeStrings.Count > 0)
                {
                    Console.WriteLine($"    ForgeTypeId strings ({forgeStrings.Count}):");
                    foreach (var s in forgeStrings.Take(10))
                        Console.WriteLine($"      {s}");
                }

                var ruStrings = new List<string>();
                for (int j = 0; j + 6 <= data.Length; j++)
                {
                    uint sLen = BitConverter.ToUInt32(data, j);
                    if (sLen < 2 || sLen > 200 || j + 4 + (int)sLen * 2 > data.Length) continue;
                    bool valid = true;
                    bool hasCyrillic = false;
                    for (int k = 0; k < (int)sLen && valid; k++)
                    {
                        byte lo = data[j + 4 + k * 2], hi = data[j + 4 + k * 2 + 1];
                        if (hi > 0x04 && hi != 0xF7) valid = false;
                        if (hi == 0 && lo < 0x20) valid = false;
                        if (hi == 0x04) hasCyrillic = true;
                    }
                    if (!valid || !hasCyrillic) continue;
                    string s = System.Text.Encoding.Unicode.GetString(data, j + 4, (int)sLen * 2);
                    if (s.Length >= 2)
                    {
                        ruStrings.Add(s);
                        j += 3 + (int)sLen * 2;
                    }
                }
                if (ruStrings.Count > 0)
                {
                    Console.Write($"    Russian strings: ");
                    Console.WriteLine(string.Join(", ", ruStrings.Take(10).Select(s => $"\"{s}\"")));
                }
            }
        }

        // Decode 0x091F: ParamElem instance data
        if (blocksByClass.TryGetValue(0x091F, out var s1fBlocks))
        {
            Console.WriteLine($"\n===== 0x091F: ParamElem Instance Data ({s1fBlocks.Count} blocks) =====");
            // All blocks are fixed-size 1249 bytes
            var sizes = s1fBlocks.Select(b => b.data.Length).Distinct().ToList();
            Console.WriteLine($"  Distinct sizes: {string.Join(", ", sizes)}");

            foreach (var (eid, data) in s1fBlocks.Take(5))
            {
                Console.Write($"  eid={eid}: ");
                // Header: 26 zeros, then FFFFFFFF, then 028D tag, then element refs
                var refs = new List<uint>();
                for (int j = 0x22; j + 4 <= Math.Min(data.Length, 0x40); j += 4)
                {
                    uint val = BitConverter.ToUInt32(data, j);
                    if (val > 0 && val < 10_000_000 && val != 0xFFFFFFFF)
                        refs.Add(val);
                }
                Console.Write($"refs=[{string.Join(",", refs)}] ");

                // Extract doubles from 0x48-0xB0 region (offset values)
                var doubles = new List<(int off, double val)>();
                for (int j = 0x44; j + 8 <= Math.Min(data.Length, 0xC0); j += 8)
                {
                    double v = BitConverter.ToDouble(data, j);
                    if (v != 0 && !double.IsNaN(v) && !double.IsInfinity(v) && Math.Abs(v) < 1e6 && Math.Abs(v) > 1e-20)
                        doubles.Add((j, v));
                }
                if (doubles.Count > 0)
                {
                    Console.Write($"doubles=[");
                    Console.Write(string.Join(", ", doubles.Take(6).Select(d => $"{d.val:G4}@0x{d.off:X}")));
                    Console.Write("] ");
                }

                // Scan for a 3x3 identity matrix: three 1.0 values at stride 32 with 0.0 between
                int matOff = FindIdentityMatrix(data);
                Console.Write(matOff >= 0 ? $"transform=IDENTITY@0x{matOff:X}" : "transform=NON-IDENTITY");
                Console.WriteLine();
            }

            int identCount = 0, nonIdentCount = 0;
            foreach (var (_, data) in s1fBlocks)
            {
                if (FindIdentityMatrix(data) >= 0) identCount++; else nonIdentCount++;
            }
            Console.WriteLine($"\n  Identity transforms: {identCount}/{s1fBlocks.Count}");
            Console.WriteLine($"  Non-identity transforms: {nonIdentCount}/{s1fBlocks.Count}");
        }

        // Decode remaining block types: structural analysis
        var analyzeTypes = new (uint classKey, string name)[]
        {
            (0x00AE, "Tf-serialized (large, 0B47 tag)"),
            (0x00AD, "Tf-serialized (large, multi-tag)"),
            (0x08F7, "Tf-serialized (small, 0B47 tag)"),
            (0x0346, "Entry-list (FF49/FF26/FF27)"),
            (0x0FE1, "Entry-list (FF26/FF27)"),
            (0x0D44, "Entry-list (FF48/FF26/FF27)"),
            (0x06EC, "ParamElem data (FF 8D02)"),
            (0x0DED, "Minimal 2-byte blocks"),
        };
        foreach (var (cKey, cName) in analyzeTypes)
        {
            if (!blocksByClass.TryGetValue(cKey, out var blocks)) continue;
            Console.WriteLine($"\n===== 0x{cKey:X4}: {cName} ({blocks.Count} blocks) =====");
            var sizes = blocks.Select(b => b.data.Length).ToList();
            Console.WriteLine($"  Size range: {sizes.Min()}-{sizes.Max()}, avg={sizes.Average():F0}");

            foreach (var (eid, data) in blocks.Take(3))
            {
                Console.Write($"  eid={eid} size={data.Length}: ");
                var tags = new List<(int off, ushort tag)>();
                for (int j = 0; j + 2 <= Math.Min(data.Length, 200); j += 2)
                {
                    ushort t = BitConverter.ToUInt16(data, j);
                    if (t == 0x0B47 || t == 0x0B49 || t == 0x0B46 || t == 0x0B48 ||
                        t == 0x0826 || t == 0x0827 || t == 0x028D || t == 0x0548)
                        tags.Add((j, t));
                }
                if (tags.Count > 0)
                    Console.Write($"tags=[{string.Join(",", tags.Take(6).Select(t => $"0x{t.tag:X4}@{t.off}"))}] ");

                var refs = new List<uint>();
                for (int j = 0; j + 4 <= Math.Min(data.Length, 100); j += 4)
                {
                    uint v = BitConverter.ToUInt32(data, j);
                    if (v > 1000 && v < 10_000_000 && v != 0xFFFFFFFF && !refs.Contains(v))
                        refs.Add(v);
                }
                if (refs.Count > 0)
                    Console.Write($"refs=[{string.Join(",", refs.Take(5))}] ");

                uint first4 = data.Length >= 4 ? BitConverter.ToUInt32(data, 0) : 0;
                Console.Write($"first4=0x{first4:X8} ");

                int strCount = 0;
                for (int j = 0; j + 6 <= data.Length; j++)
                {
                    uint sLen = BitConverter.ToUInt32(data, j);
                    if (sLen >= 3 && sLen < 200 && j + 4 + (int)sLen * 2 <= data.Length)
                    {
                        bool valid = true;
                        for (int k = 0; k < (int)sLen && valid; k++)
                        {
                            byte hi = data[j + 4 + k * 2 + 1];
                            if (hi > 0x04 && hi != 0xF7) valid = false;
                        }
                        if (valid) { strCount++; j += 3 + (int)sLen * 2; }
                    }
                }
                Console.Write($"strings={strCount} ");

                int dblCount = 0;
                for (int j = 0; j + 8 <= Math.Min(data.Length, 200); j += 8)
                {
                    double v = BitConverter.ToDouble(data, j);
                    if (v != 0 && !double.IsNaN(v) && !double.IsInfinity(v) && Math.Abs(v) > 1e-20 && Math.Abs(v) < 1e12)
                        dblCount++;
                }
                Console.Write($"doubles={dblCount}");
                Console.WriteLine();
            }

            if (cKey == 0x0DED)
            {
                var valueCounts = new Dictionary<string, int>();
                foreach (var (_, data) in blocks)
                {
                    string hex = BitConverter.ToString(data);
                    valueCounts.TryGetValue(hex, out int c);
                    valueCounts[hex] = c + 1;
                }
                Console.WriteLine($"  Distinct values: {valueCounts.Count}");
                foreach (var (hex, cnt) in valueCounts.OrderByDescending(kv => kv.Value).Take(10))
                    Console.WriteLine($"    {hex}: {cnt} blocks");
            }
        }

        return 0;
    }

    // --decode-catc : deep-decode remaining Category C/E blocks
    private static int DecodeCatc(string rvtPath)
    {
        var readerC = new RvtFileReader(new RvtReaderOptions { DiagnosticOutput = Console.Error });
        var (_, stateC) = readerC.ReadWithState(rvtPath);

        var allBlocks = new Dictionary<uint, List<(uint eid, byte[] data, uint fullClassIdx)>>();
        foreach (var chunk in stateC.NonRecordChunks.Concat(stateC.RecordChunks))
        {
            int pos = 0;
            while (pos + 16 <= chunk.Length)
            {
                uint eid = BitConverter.ToUInt32(chunk, pos);
                if (pos + 12 <= chunk.Length)
                {
                    uint rBodySize = BitConverter.ToUInt32(chunk, pos + 4);
                    ushort rMagic = BitConverter.ToUInt16(chunk, pos + 8);
                    if ((rMagic == 0x053C || rMagic == 0x04F8) && eid > 0 && eid < 10_000_000
                        && rBodySize > 0 && rBodySize < 500_000 && pos + 12 + (int)rBodySize <= chunk.Length)
                    { pos += 12 + (int)rBodySize; continue; }
                }
                uint dataSize = BitConverter.ToUInt32(chunk, pos + 8);
                if (dataSize > 1_000_000) { pos++; continue; }
                if (eid == 0) { pos += 4; continue; }
                if (dataSize == 0) { pos++; continue; }
                if (pos + 16 + (int)dataSize > chunk.Length) break;
                uint classIdx = BitConverter.ToUInt32(chunk, pos + 12);
                uint classKey = classIdx & 0xFFFF;
                if (eid > 0 && eid < 10_000_000)
                {
                    if (!allBlocks.TryGetValue(classKey, out var list))
                        allBlocks[classKey] = list = new List<(uint, byte[], uint)>();
                    var data = new byte[(int)dataSize];
                    Array.Copy(chunk, pos + 16, data, 0, (int)dataSize);
                    list.Add((eid, data, classIdx));
                }
                pos += 16 + (int)dataSize;
            }
        }

        // Target classIdx values: undecoded Category C + Category E + remaining Category A
        uint[] targets = {
            0x0786, 0x03FC, 0x0B3A, 0x0E17, 0x0B84, 0x0855, 0x05BF, 0x07C6,
            0x0722, 0x08CB, 0x0DDA, 0x0EC4, 0x0F26, 0x0F83, 0x0BFD, 0x09B6,
            0x0158, 0x0A44, 0x03F4, 0x009D,  // Cat C
            0x0931, 0x0CB7, 0x0CBF, 0x0C83, 0x0C6F, 0x0CC7, 0x0C93,  // Cat E
            0x03F8, 0x03E3, 0x03F2, 0x03ED, 0x08F8, 0x026D, 0x0744,  // Cat A remaining
            0x03DA, 0x0F57, 0x0746, 0x08FB, 0x08FD, 0x0D4E,  // Cat A remaining
            0x0000, 0x0001, 0x01BD, 0x0890, 0x0C01  // Cat F
        };

        foreach (var cKey in targets)
        {
            if (!allBlocks.TryGetValue(cKey, out var blocks) || blocks.Count == 0) continue;

            var sizes = blocks.Select(b => b.data.Length).Distinct().OrderBy(x => x).ToList();
            var fullClassIdxes = blocks.Select(b => b.fullClassIdx).Distinct().OrderBy(x => x).ToList();
            Console.WriteLine($"\n===== 0x{cKey:X4}: {blocks.Count} blocks, sizes {sizes.First()}-{sizes.Last()} ({sizes.Count} distinct) =====");
            Console.WriteLine($"  fullClassIdx: {string.Join(", ", fullClassIdxes.Take(10).Select(x => $"0x{x:X8}"))}");

            foreach (var (eid, data, fci) in blocks.Take(3))
            {
                Console.WriteLine($"\n  Block eid={eid}, size={data.Length}, classIdx=0x{fci:X8}");
                int dumpLen = Math.Min(data.Length, 256);
                for (int row = 0; row < dumpLen; row += 16)
                {
                    Console.Write($"    {row:X4}: ");
                    var hexParts = new List<string>();
                    var ascii = new List<char>();
                    for (int col = 0; col < 16 && row + col < dumpLen; col++)
                    {
                        hexParts.Add($"{data[row + col]:X2}");
                        byte b = data[row + col];
                        ascii.Add(b >= 0x20 && b < 0x7F ? (char)b : '.');
                    }
                    Console.Write(string.Join(" ", hexParts).PadRight(48));
                    Console.Write("  ");
                    Console.WriteLine(new string(ascii.ToArray()));
                }

                // Find all uint16 tags that match known tags
                var knownTags = new HashSet<ushort> {
                    0x0B47, 0x0B49, 0x0B46, 0x0B48, 0x0826, 0x0827, 0x028D, 0x02CA,
                    0x0548, 0x091F, 0x0725, 0x06EC, 0x0802
                };
                var foundTags = new List<(int off, ushort tag)>();
                for (int j = 0; j + 2 <= data.Length; j += 2)
                {
                    ushort t = BitConverter.ToUInt16(data, j);
                    if (knownTags.Contains(t)) foundTags.Add((j, t));
                }
                if (foundTags.Count > 0)
                    Console.WriteLine($"    Tags: {string.Join(" ", foundTags.Select(t => $"0x{t.tag:X4}@{t.off}"))}");

                var refs = new List<(int off, uint val)>();
                for (int j = 0; j + 4 <= data.Length; j += 4)
                {
                    uint v = BitConverter.ToUInt32(data, j);
                    if (v > 200_000 && v < 500_000) refs.Add((j, v));
                }
                if (refs.Count > 0 && refs.Count <= 30)
                    Console.WriteLine($"    Refs({refs.Count}): {string.Join(" ", refs.Select(r => $"{r.val}@0x{r.off:X}"))}");
                else if (refs.Count > 30)
                    Console.WriteLine($"    Refs({refs.Count}): {string.Join(" ", refs.Take(10).Select(r => $"{r.val}@0x{r.off:X}"))} ...");

                var dbls = new List<(int off, double val)>();
                for (int j = 0; j + 8 <= data.Length; j += 4)
                {
                    double v = BitConverter.ToDouble(data, j);
                    if (v != 0 && !double.IsNaN(v) && !double.IsInfinity(v) && Math.Abs(v) > 1e-15 && Math.Abs(v) < 1e9)
                        dbls.Add((j, v));
                }
                if (dbls.Count > 0 && dbls.Count <= 20)
                    Console.WriteLine($"    Doubles({dbls.Count}): {string.Join(" ", dbls.Select(d => $"{d.val:G6}@0x{d.off:X}"))}");
                else if (dbls.Count > 20)
                    Console.WriteLine($"    Doubles({dbls.Count}): {string.Join(" ", dbls.Take(10).Select(d => $"{d.val:G6}@0x{d.off:X}"))} ...");

                var strs = new List<(int off, string val)>();
                for (int j = 0; j + 4 <= data.Length; j++)
                {
                    uint sLen = BitConverter.ToUInt32(data, j);
                    if (sLen > 1 && sLen < 500 && j + 4 + (int)sLen * 2 <= data.Length)
                    {
                        bool valid = true;
                        for (int k = 0; k < (int)sLen && valid; k++)
                        {
                            byte hi = data[j + 4 + k * 2 + 1];
                            if (hi > 0x04 && hi != 0xF7) valid = false;
                        }
                        if (valid)
                        {
                            string s = System.Text.Encoding.Unicode.GetString(data, j + 4, (int)sLen * 2);
                            if (s.Length > 2 && s.All(c => c >= 0x20))
                            {
                                strs.Add((j, s.Length > 80 ? s[..80] + "..." : s));
                                j += 3 + (int)sLen * 2;
                            }
                        }
                    }
                }
                if (strs.Count > 0 && strs.Count <= 15)
                    Console.WriteLine($"    Strings({strs.Count}): {string.Join("; ", strs.Select(s => $"\"{s.val}\"@0x{s.off:X}"))}");
                else if (strs.Count > 15)
                    Console.WriteLine($"    Strings({strs.Count}): {string.Join("; ", strs.Take(8).Select(s => $"\"{s.val}\"@0x{s.off:X}"))} ...");

                // Pattern: all zeros, then 01 00 ending (Category C signature)
                int firstNonZero = -1;
                for (int j = 0; j < data.Length; j++)
                {
                    if (data[j] != 0 && data[j] != 0xFF) { firstNonZero = j; break; }
                }
                if (firstNonZero >= 0)
                    Console.WriteLine($"    FirstNonZero@0x{firstNonZero:X} = 0x{data[firstNonZero]:X2}");

                var bips = new List<(int off, int val)>();
                for (int j = 0; j + 4 <= data.Length; j += 4)
                {
                    int v = BitConverter.ToInt32(data, j);
                    if (v < -1000 && v > -2_000_000) bips.Add((j, v));
                }
                if (bips.Count > 0 && bips.Count <= 20)
                    Console.WriteLine($"    BIPs({bips.Count}): {string.Join(" ", bips.Select(b => $"{b.val}@0x{b.off:X}"))}");
                else if (bips.Count > 20)
                    Console.WriteLine($"    BIPs({bips.Count}): {string.Join(" ", bips.Take(10).Select(b => $"{b.val}@0x{b.off:X}"))} ...");
            }

            if (sizes.Count == 1)
                Console.WriteLine($"\n  ALL blocks are exactly {sizes[0]} bytes");
            else if (sizes.Count <= 5)
                Console.WriteLine($"\n  Size distribution: {string.Join(", ", sizes.Select(s => $"{s}({blocks.Count(b => b.data.Length == s)})"))}");

            var first4patterns = new Dictionary<string, int>();
            foreach (var (_, data, _) in blocks)
            {
                string pat = data.Length >= 4 ? $"{data[0]:X2}{data[1]:X2}{data[2]:X2}{data[3]:X2}" : "short";
                first4patterns.TryGetValue(pat, out int c2);
                first4patterns[pat] = c2 + 1;
            }
            Console.WriteLine($"  First4 patterns: {string.Join(", ", first4patterns.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{kv.Key}({kv.Value})"))}");

            var hiWords = blocks.Select(b => (b.fullClassIdx >> 16)).Distinct().OrderBy(x => x).ToList();
            if (hiWords.Count <= 5)
                Console.WriteLine($"  ClassIdx high words: {string.Join(", ", hiWords.Select(h => $"0x{h:X4}"))}");
            else
                Console.WriteLine($"  ClassIdx high words: {hiWords.Count} distinct, range 0x{hiWords.First():X4}-0x{hiWords.Last():X4}");
        }

        return 0;
    }

    private static int SearchGlobal(string rvtPath, string searchStream)
    {
        // Search for UTF-16LE string in Global/* streams
        var target2 = System.Text.Encoding.Unicode.GetBytes(searchStream);
        using var rootFs3 = OpenMcdf.RootStorage.OpenRead(rvtPath);
        var globalStor = rootFs3.OpenStorage("Global");
        foreach (var entry in globalStor.EnumerateEntries())
        {
            Console.Error.WriteLine($"Global/{entry.Name} ({entry.Type}, {entry.Length} bytes)");
            if (entry.Type != OpenMcdf.EntryType.Stream) continue;
            try
            {
                using var stream = globalStor.OpenStream(entry.Name);
                var data = new byte[stream.Length];
                stream.ReadExactly(data);

                for (int pos = 0; pos <= data.Length - target2.Length; pos++)
                {
                    bool match = true;
                    for (int j = 0; j < target2.Length && match; j++)
                        if (data[pos + j] != target2[j]) match = false;
                    if (match)
                        Console.WriteLine($"  Found in Global/{entry.Name} (raw) at offset 0x{pos:X}");
                }

                try
                {
                    var dc2 = new RvtIO.Parsing.ChunkDecompressor();
                    var chunks2 = dc2.DecompressAll(data);
                    foreach (var (ci2, chunk2) in chunks2.Select((c, i) => (i, c)))
                    {
                        for (int pos = 0; pos <= chunk2.Length - target2.Length; pos++)
                        {
                            bool match = true;
                            for (int j = 0; j < target2.Length && match; j++)
                                if (chunk2[pos + j] != target2[j]) match = false;
                            if (match)
                                Console.WriteLine($"  Found in Global/{entry.Name} (decompressed chunk[{ci2}]) at offset 0x{pos:X}");
                        }
                    }
                }
                catch (Exception ex) when (RvtIO.Parsing.MalformedInput.Is(ex)) { /* not gzip */ }

                if (data.Length > 10)
                {
                    try
                    {
                        using var ms = new MemoryStream(data, 10, data.Length - 10);
                        using var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress);
                        using var outMs = new MemoryStream();
                        ds.CopyTo(outMs);
                        var deflData = outMs.ToArray();
                        for (int pos = 0; pos <= deflData.Length - target2.Length; pos++)
                        {
                            bool match = true;
                            for (int j = 0; j < target2.Length && match; j++)
                                if (deflData[pos + j] != target2[j]) match = false;
                            if (match)
                            {
                                int strlen = pos >= 4 ? BitConverter.ToInt32(deflData, pos - 4) : -1;
                                Console.WriteLine($"  Found in Global/{entry.Name} (deflate) at offset 0x{pos:X} strlen={strlen}");
                                if (strlen > 0 && strlen < 500 && pos + strlen * 2 <= deflData.Length)
                                {
                                    try { Console.WriteLine($"    Full: \"{System.Text.Encoding.Unicode.GetString(deflData, pos, strlen * 2)}\""); }
                                    catch (Exception ex) when (RvtIO.Parsing.MalformedInput.Is(ex)) { /* not a valid UTF-16 string */ }
                                }
                            }
                        }
                    }
                    catch (Exception ex) when (RvtIO.Parsing.MalformedInput.Is(ex)) { /* not deflate */ }
                }
            }
            catch (Exception ex) { Console.Error.WriteLine($"  Error: {ex.Message}"); }
        }

        try
        {
            var fmtStor = rootFs3.OpenStorage("Formats");
            foreach (var entry in fmtStor.EnumerateEntries())
            {
                Console.Error.WriteLine($"Formats/{entry.Name} ({entry.Type}, {entry.Length} bytes)");
                if (entry.Type != OpenMcdf.EntryType.Stream) continue;
                try
                {
                    using var stream = fmtStor.OpenStream(entry.Name);
                    var data = new byte[stream.Length];
                    stream.ReadExactly(data);
                    for (int pos = 0; pos <= data.Length - target2.Length; pos++)
                    {
                        bool match = true;
                        for (int j = 0; j < target2.Length && match; j++)
                            if (data[pos + j] != target2[j]) match = false;
                        if (match)
                            Console.WriteLine($"  Found in Formats/{entry.Name} (raw) at offset 0x{pos:X}");
                    }
                    try
                    {
                        var dc2 = new RvtIO.Parsing.ChunkDecompressor();
                        var chunks2 = dc2.DecompressAll(data);
                        foreach (var (ci2, chunk2) in chunks2.Select((c, i) => (i, c)))
                        {
                            for (int pos = 0; pos <= chunk2.Length - target2.Length; pos++)
                            {
                                bool match = true;
                                for (int j = 0; j < target2.Length && match; j++)
                                    if (chunk2[pos + j] != target2[j]) match = false;
                                if (match)
                                {
                                    int strlen = pos >= 4 ? BitConverter.ToInt32(chunk2, pos - 4) : -1;
                                    Console.WriteLine($"  Found in Formats/{entry.Name} (chunk[{ci2}]) at offset 0x{pos:X} strlen={strlen}");
                                    if (strlen > 0 && strlen < 500 && pos + strlen * 2 <= chunk2.Length)
                                        try { Console.WriteLine($"    Full: \"{System.Text.Encoding.Unicode.GetString(chunk2, pos, strlen * 2)}\""); } catch (Exception ex) when (RvtIO.Parsing.MalformedInput.Is(ex)) { /* not a valid UTF-16 string */ }
                                }
                            }
                        }
                    }
                    catch (Exception ex) when (RvtIO.Parsing.MalformedInput.Is(ex)) { /* not gzip */ }
                }
                catch (Exception ex) { Console.Error.WriteLine($"  Error: {ex.Message}"); }
            }
        }
        catch (Exception ex) when (RvtIO.Parsing.MalformedInput.Is(ex)) { Console.Error.WriteLine("No Formats storage"); }

        return 0;
    }

    private static int FindStr(string rvtPath, string searchStr)
    {
        // Search for UTF-16LE string in decompressed partition chunks (with Deflate64 artifact tolerance)
        using var rootFs = OpenMcdf.RootStorage.OpenRead(rvtPath);
        var partStor = rootFs.OpenStorage("Partitions");
        var partEntries = partStor.EnumerateEntries()
            .Where(e => e.Type == OpenMcdf.EntryType.Stream)
            .OrderByDescending(e => e.Length).ToList();
        using var partStream = partStor.OpenStream(partEntries[0].Name);
        var rawData = new byte[partStream.Length];
        partStream.ReadExactly(rawData);
        var decompressor = new RvtIO.Parsing.ChunkDecompressor();
        var allChunks = decompressor.DecompressAll(rawData);
        Console.Error.WriteLine($"Searching {allChunks.Count} chunks for '{searchStr}' (with Deflate64 tolerance)...");
        // Build search pattern: for each char, accept original UTF-16LE OR Deflate64-corrupted (hi=0xF7 for Cyrillic)
        var searchChars = searchStr.ToCharArray();
        bool MatchChar(byte lo, byte hi, char expected)
        {
            if (expected < 0x80) return hi == 0x00 && lo == expected; // ASCII
            if (expected >= 0x0400 && expected <= 0x04FF)
            {
                byte expLo = (byte)(expected & 0xFF);
                return (hi == 0x04 && lo == expLo) || (hi == 0xF7 && lo == expLo) || (hi == 0x1B && lo == expLo);
            }
            return hi == (byte)(expected >> 8) && lo == (byte)(expected & 0xFF);
        }
        var target = System.Text.Encoding.Unicode.GetBytes(searchStr); // for fallback
        for (int ci = 0; ci < allChunks.Count; ci++)
        {
            var chunk = allChunks[ci];
            for (int pos = 0; pos <= chunk.Length - searchChars.Length * 2; pos++)
            {
                bool match = true;
                for (int k = 0; k < searchChars.Length && match; k++)
                    if (!MatchChar(chunk[pos + k * 2], chunk[pos + k * 2 + 1], searchChars[k])) match = false;
                if (!match) continue;
                int strlen = pos >= 4 ? BitConverter.ToInt32(chunk, pos - 4) : -1;
                Console.Write($"Found in chunk[{ci}] at offset 0x{pos:X} (strlen_prefix={strlen})");
                if (strlen > 0 && strlen < 500 && pos - 4 + 4 + strlen * 2 <= chunk.Length)
                {
                    var sb2 = new System.Text.StringBuilder();
                    for (int k = 0; k < strlen; k++)
                    {
                        byte lo2 = chunk[pos + k * 2], hi2 = chunk[pos + k * 2 + 1];
                        if (hi2 == 0x04) sb2.Append((char)(0x0400 | lo2));
                        else if (hi2 == 0xF7) sb2.Append((char)lo2);
                        else sb2.Append((char)(hi2 * 256 + lo2));
                    }
                    Console.Write($" \"{sb2}\"");
                }
                Console.WriteLine();
                for (int scan = Math.Max(0, pos - 500); scan < pos; scan++)
                {
                    uint eid = BitConverter.ToUInt32(chunk, scan);
                    if (eid > 0 && eid < 10_000_000)
                    {
                        // Check if this is a block header: [eid:4][hash:4][size:4][classIdx:4]
                        if (scan + 16 <= chunk.Length)
                        {
                            int dSize = BitConverter.ToInt32(chunk, scan + 8);
                            if (dSize > 0 && dSize < 100000 && scan + 16 + dSize > pos)
                                Console.WriteLine($"  Possible block: eid={eid} at 0x{scan:X}, size={dSize}, classIdx=0x{BitConverter.ToUInt32(chunk, scan + 12):X8}");
                        }
                        if (scan + 12 <= chunk.Length)
                        {
                            uint rSize = BitConverter.ToUInt32(chunk, scan + 4);
                            ushort magic = BitConverter.ToUInt16(chunk, scan + 8);
                            if ((magic == 0x053C || magic == 0x04F8) && rSize > 0 && rSize < 500000 && scan + 12 + (int)rSize > pos)
                                Console.WriteLine($"  RECORD: eid={eid} at 0x{scan:X}, bodySize={rSize}, magic=0x{magic:X4}, str at body offset 0x{pos - scan - 12:X}");
                        }
                    }
                }
            }
        }
        return 0;
    }


    // --parse-block <block.bin>...: decode raw GRep block files ([eid][hash][size][class][payload][size], as
    // saved by RVTIO_DUMP_GREP or cut out of a decompressed partition stream) without opening an RVT.
    private static int ParseBlock(string[] args)
    {
        foreach (string blockPath in args.Skip(1))
        {
            byte[] blockData = File.ReadAllBytes(blockPath);
            uint blockSize = blockData.Length >= 12 ? BitConverter.ToUInt32(blockData, 8) : 0;
            var decoded = blockData.Length >= 16 && 16L + blockSize <= blockData.Length
                ? RvtIO.Parsing.GRepParserV2.Parse(blockData, 0, blockSize) : null;
            Console.WriteLine(decoded == null
                ? $"{blockPath}: decoded=null (bytes={blockData.Length})"
                : $"{blockPath}: faces={decoded.Faces.Count} solids={decoded.Solids.Count} "
                    + $"closed={decoded.Solids.Count(solid => solid.IsClosed)} instances={decoded.Instances.Count} "
                    + $"bbox=[{string.Join(',', decoded.BoundingBox ?? [])}]");
        }
        return 0;
    }

    // Scan for a 3x3 identity matrix in data: three 1.0 doubles at stride 32 with all intervening doubles = 0.0
    private static int FindIdentityMatrix(byte[] data)
    {
        // Pattern: [1.0:8][0:8][0:8][0:8] [0:8][1.0:8][0:8][0:8] [0:8][0:8][1.0:8][0:8] = 96 bytes, stride 32
        for (int i = 0; i + 96 <= data.Length; i++)
        {
            double d0 = BitConverter.ToDouble(data, i);
            if (Math.Abs(d0 - 1.0) > 1e-10) continue;
            if (i + 64 + 8 > data.Length) continue;
            double d1 = BitConverter.ToDouble(data, i + 32);
            double d2 = BitConverter.ToDouble(data, i + 64);
            if (Math.Abs(d1 - 1.0) > 1e-10 || Math.Abs(d2 - 1.0) > 1e-10) continue;
            bool allZero = true;
            for (int k = 0; k < 12 && allZero; k++)
            {
                int off = i + k * 8;
                if (k == 0 || k == 5 || k == 10) continue; // diagonal elements
                double v = BitConverter.ToDouble(data, off);
                if (Math.Abs(v) > 1e-10) allZero = false;
            }
            if (allZero) return i;
        }
        return -1;
    }
}
