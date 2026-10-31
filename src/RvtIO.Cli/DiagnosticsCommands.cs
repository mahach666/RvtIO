using System.Text.Json;
using System.Text.Json.Serialization;
using RvtIO;
using RvtIO.Compact;
using RvtIO.BuiltIns;
using RvtIO.Ifc;
using RvtIO.Model;

internal static partial class Program
{
    // --elemtable-diag: diagnostics-only harness for the ElemTable bit-desync recovery. Loads just the
    // Global/ElemTable stream and drives ElemTableRecover with instrumentation enabled.
    // Flags: --wide (run the relaxed wide scan at the failing desync)
    private static int ElemTableDiag(string[] args, string rvtPath)
    {
        var swTotal = System.Diagnostics.Stopwatch.StartNew();
        byte[] etData;
        if (rvtPath.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
        {
            etData = File.ReadAllBytes(rvtPath);   // raw ElemTable stream dumped earlier
        }
        else
        {
            using var etRoot = OpenMcdf.RootStorage.OpenRead(rvtPath);
            try
            {
                var etGlobal = etRoot.OpenStorage("Global");
                using var etStream = etGlobal.OpenStream("ElemTable");
                etData = new byte[etStream.Length];
                etStream.ReadExactly(etData);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ElemTable stream unavailable: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        int etGz = -1;
        for (int ii = 0; ii < etData.Length - 2; ii++)
            if (etData[ii] == 0x1F && etData[ii + 1] == 0x8B) { etGz = ii; break; }
        uint etIsize = BitConverter.ToUInt32(etData, etData.Length - 4);
        uint etCrc = BitConverter.ToUInt32(etData, etData.Length - 8);
        Console.Error.WriteLine($"=== {System.IO.Path.GetFileName(rvtPath)} ===");
        Console.Error.WriteLine($"ElemTable raw stream = {etData.Length} bytes, gzStart = {etGz}, " +
                                $"ISIZE@end = {etIsize}, CRC32@end = 0x{etCrc:X8}");
        if (etGz < 0) { Console.Error.WriteLine("no gzip magic"); return 1; }
        {
            int z = etData.Length;
            while (z > 0 && etData[z - 1] == 0) z--;
            Console.Error.WriteLine($"  trailing zero bytes = {etData.Length - z} (last non-zero at {z - 1})");
            int h0 = Math.Max(0, etData.Length - 48);
            Console.Error.WriteLine($"  last 48 raw bytes: {BitConverter.ToString(etData, h0, etData.Length - h0)}");
            int gz2 = -1;
            for (int ii = etGz + 2; ii < etData.Length - 2; ii++)
                if (etData[ii] == 0x1F && etData[ii + 1] == 0x8B && etData[ii + 2] == 8) { gz2 = ii; break; }
            Console.Error.WriteLine($"  next gzip magic after first: {gz2}");
        }
        if (args.Contains("--no-recover")) { Console.Error.WriteLine($"total {swTotal.Elapsed.TotalSeconds:F1}s"); return 0; }

        byte[]? etGzip = null;
        var swGzip = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var ms = new MemoryStream();
            using var gs = new System.IO.Compression.GZipStream(
                new MemoryStream(etData, etGz, etData.Length - etGz), System.IO.Compression.CompressionMode.Decompress);
            gs.CopyTo(ms);
            etGzip = ms.ToArray();
            Console.Error.WriteLine($"GZipStream: {etGzip.Length} bytes in {swGzip.Elapsed.TotalSeconds:F1}s (clean)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"GZipStream: threw {ex.GetType().Name}: {ex.Message} after {swGzip.Elapsed.TotalSeconds:F1}s");
        }

        if (etGzip is { Length: > 64 })
            Console.Error.WriteLine("  gzip census: " + RvtIO.Parsing.ElemTableRecover.DiagRecordCensus(etGzip, 0, etGzip.Length));

        // Locate the REAL end of the gzip member: the emitted tail is always
        // <sync-flush 00 00 FF FF><02 08 30 00><CRC32:4><ISIZE:4> in these Revit streams.
        int memberEnd = -1;
        {
            byte[] sig = { 0x00, 0x00, 0xFF, 0xFF, 0x02, 0x08, 0x30, 0x00 };
            for (int q = etData.Length - sig.Length - 8; q >= 0; q--)
            {
                bool m = true;
                for (int j = 0; j < sig.Length; j++) if (etData[q + j] != sig[j]) { m = false; break; }
                if (m)
                {
                    int tr = q + sig.Length;
                    uint c = BitConverter.ToUInt32(etData, tr), s = BitConverter.ToUInt32(etData, tr + 4);
                    Console.Error.WriteLine($"  TRUE gzip trailer at {tr}: CRC=0x{c:X8} ISIZE={s} " +
                                            $"(member ends {tr + 8}; raw len {etData.Length}; " +
                                            $"{etData.Length - (tr + 8)} trailing bytes ignored by TryReadGzipMetadata)");
                    memberEnd = tr + 8;
                    break;
                }
            }
            if (memberEnd < 0) Console.Error.WriteLine("  TRUE gzip trailer signature not found");
        }

        if (!args.Contains("--no-d64"))
        {
            var swD = System.Diagnostics.Stopwatch.StartNew();
            byte[]? d64 = null;
            try { d64 = RvtIO.Parsing.ChunkDecompressor.DecompressDeflate64Gzip(etData, etGz, etData.Length - etGz); }
            catch (Exception ex) { Console.Error.WriteLine($"  Deflate64 threw {ex.GetType().Name}: {ex.Message}"); }
            Console.Error.WriteLine($"  Deflate64/DeflateStream fallback: {(d64 == null ? "null" : d64.Length.ToString())} bytes " +
                                    $"in {swD.Elapsed.TotalSeconds:F1}s" +
                                    (d64 == null ? "" : $" CRC=0x{RvtIO.Parsing.ElemTableRecover.DiagCrc32(d64):X8}"));
        }

        // Re-run on the stream trimmed to the real member end so that TryReadGzipMetadata reads the correct CRC/ISIZE.
        if (args.Contains("--trim") && memberEnd > 0)
        {
            var trimmed = new byte[memberEnd];
            Buffer.BlockCopy(etData, 0, trimmed, 0, memberEnd);
            etData = trimmed;
            etIsize = BitConverter.ToUInt32(etData, etData.Length - 4);
            Console.Error.WriteLine($"  [--trim] raw trimmed to {etData.Length}; ISIZE now {etIsize}");
        }

        if (etIsize > 0 && etGzip != null && (uint)etGzip.Length == etIsize)
        {
            Console.Error.WriteLine("ElemTable decompresses cleanly: recovery path never fires on this file.");
            Console.Error.WriteLine($"total {swTotal.Elapsed.TotalSeconds:F1}s");
            return 0;
        }

        // --sweep <n>: force desync #n's relock to each delta in [-256,1400) in turn, run the whole
        // algorithm to completion, and check the result against the TRUE trailer CRC/ISIZE.
        int sweepAt = -1;
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--sweep") sweepAt = int.Parse(args[i + 1]);
        if (sweepAt > 0 && memberEnd > 0)
        {
            uint trueCrc = BitConverter.ToUInt32(etData, memberEnd - 8);
            uint trueSize = BitConverter.ToUInt32(etData, memberEnd - 4);
            Console.Error.WriteLine($"[sweep] desync #{sweepAt}; target ISIZE={trueSize} CRC=0x{trueCrc:X8}");
            int pin = 910;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--pin1") pin = int.Parse(args[i + 1]);
            RvtIO.Parsing.ElemTableRecover.Diag = TextWriter.Null;
            var swSweep = System.Diagnostics.Stopwatch.StartNew();
            var results = new List<(int Delta, int Size, uint Crc)>();
            int matches = 0;
            for (int delta = -256; delta < 1400; delta++)
            {
                RvtIO.Parsing.ElemTableRecover.DiagForceRelock.Clear();
                if (sweepAt == 2) RvtIO.Parsing.ElemTableRecover.DiagForceRelock[1] = pin;
                RvtIO.Parsing.ElemTableRecover.DiagForceRelock[sweepAt] = delta;
                RvtIO.Parsing.ElemTableRecover.DiagRelocks.Clear();
                RvtIO.Parsing.ElemTableRecover.DiagPartialOutput = null;
                try { RvtIO.Parsing.ElemTableRecover.Recover(etData, etGz); } catch (Exception ex) when (RvtIO.Parsing.MalformedInput.Is(ex)) { /* a failed relock attempt leaves DiagPartialOutput unset */ }
                var pp = RvtIO.Parsing.ElemTableRecover.DiagPartialOutput;
                if (pp == null || pp.Length < 1000) continue;
                uint cc = RvtIO.Parsing.ElemTableRecover.DiagCrc32(pp);
                results.Add((delta, pp.Length, cc));
                if (cc == trueCrc && (uint)pp.Length == trueSize)
                {
                    matches++;
                    Console.Error.WriteLine($"[sweep] *** EXACT MATCH at delta={delta}: size={pp.Length} CRC=0x{cc:X8}");
                }
            }
            Console.Error.WriteLine($"[sweep] {results.Count} deltas produced output, {matches} exact CRC+size matches, " +
                                    $"{swSweep.Elapsed.TotalSeconds:F0}s");
            foreach (var r in results.OrderBy(r => Math.Abs((long)r.Size - trueSize)).Take(15))
                Console.Error.WriteLine($"  [sweep] delta={r.Delta} size={r.Size} (off by {(long)r.Size - trueSize}) CRC=0x{r.Crc:X8}");
            RvtIO.Parsing.ElemTableRecover.DiagForceRelock.Clear();
            Console.Error.WriteLine($"total {swTotal.Elapsed.TotalSeconds:F1}s");
            return 0;
        }

        // --force "1:910,2:37789" - pin specific relock deltas and report size/CRC vs the true trailer.
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--force")
                foreach (var part in args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split(':');
                    RvtIO.Parsing.ElemTableRecover.DiagForceRelock[int.Parse(kv[0])] = long.Parse(kv[1]);
                }

        RvtIO.Parsing.ElemTableRecover.Diag = Console.Error;
        RvtIO.Parsing.ElemTableRecover.DiagWideScan = args.Contains("--wide");
        RvtIO.Parsing.ElemTableRecover.DiagBlockLog = args.Contains("--blocks");
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--wide-at") RvtIO.Parsing.ElemTableRecover.DiagWideAt = int.Parse(args[i + 1]);
            if (args[i] == "--wide-lo") RvtIO.Parsing.ElemTableRecover.DiagWideLo = int.Parse(args[i + 1]);
            if (args[i] == "--wide-hi") RvtIO.Parsing.ElemTableRecover.DiagWideHi = int.Parse(args[i + 1]);
        }
        var swRec = System.Diagnostics.Stopwatch.StartNew();
        var rec = RvtIO.Parsing.ElemTableRecover.Recover(etData, etGz);
        swRec.Stop();
        Console.Error.WriteLine($"Recover() returned {(rec == null ? "null" : rec.Length + " bytes")} " +
                                $"in {swRec.Elapsed.TotalSeconds:F2}s");
        Console.Error.WriteLine($"deflate blocks entered = {RvtIO.Parsing.ElemTableRecover.DiagBlocks}, " +
                                $"relock scans = {RvtIO.Parsing.ElemTableRecover.DiagRelocks.Count}, " +
                                $"total probe bytes decoded = {RvtIO.Parsing.ElemTableRecover.DiagProbeSymbolBudget}");
        var partial = RvtIO.Parsing.ElemTableRecover.DiagPartialOutput;
        if (partial != null)
        {
            Console.Error.WriteLine($"partial output = {partial.Length} bytes " +
                                    $"({100.0 * partial.Length / Math.Max(1u, etIsize):F2}% of ISIZE)");
            Console.Error.WriteLine("  partial census (all): " + RvtIO.Parsing.ElemTableRecover.DiagRecordCensus(partial, 0, partial.Length));
            int tailFrom = Math.Max(0, partial.Length - 300000);
            Console.Error.WriteLine("  partial census (last 300k): " + RvtIO.Parsing.ElemTableRecover.DiagRecordCensus(partial, tailFrom, partial.Length));
            int hexFrom = Math.Max(0, partial.Length - 128);
            Console.Error.WriteLine($"  last 128 bytes @{hexFrom}: " + BitConverter.ToString(partial, hexFrom, partial.Length - hexFrom));

            uint pcrc = RvtIO.Parsing.ElemTableRecover.DiagCrc32(partial);
            var want = new byte[8];
            BitConverter.GetBytes(pcrc).CopyTo(want, 0);
            BitConverter.GetBytes((uint)partial.Length).CopyTo(want, 4);
            Console.Error.WriteLine($"  CRC32(partial) = 0x{pcrc:X8}; looking for trailer {BitConverter.ToString(want)}");
            int at = -1;
            for (int q = 0; q + 8 <= etData.Length; q++)
            {
                bool m = true;
                for (int j = 0; j < 8; j++) if (etData[q + j] != want[j]) { m = false; break; }
                if (m) { at = q; break; }
            }
            if (at >= 0)
            {
                Console.Error.WriteLine($"  *** REAL gzip trailer found at raw offset {at}; gzip member ends at {at + 8}; " +
                                        $"raw stream is {etData.Length} => {etData.Length - (at + 8)} trailing bytes past the member");
                int tf = at + 8;
                Console.Error.WriteLine($"  bytes after member ({etData.Length - tf}): " +
                                        BitConverter.ToString(etData, tf, Math.Min(160, etData.Length - tf)));
                Console.Error.WriteLine($"  16 bytes before trailer: " + BitConverter.ToString(etData, Math.Max(0, at - 16), 16));
            }
            else Console.Error.WriteLine("  *** no matching trailer found in the raw stream (recovered bytes are NOT the whole member)");

            if (memberEnd > 0)
            {
                uint tc = BitConverter.ToUInt32(etData, memberEnd - 8), ts = BitConverter.ToUInt32(etData, memberEnd - 4);
                Console.Error.WriteLine($"  vs TRUE trailer: size {partial.Length} vs {ts} (off by {(long)partial.Length - ts}), " +
                                        $"CRC 0x{pcrc:X8} vs 0x{tc:X8} -> {(pcrc == tc && (uint)partial.Length == ts ? "MATCH" : "MISMATCH")}");
                var pref = RvtIO.Parsing.ElemTableRecover.DiagCrcPrefixMatches(partial, tc);
                Console.Error.WriteLine($"  prefix lengths of the recovered buffer whose CRC32 == the true CRC: " +
                                        (pref.Count == 0 ? "NONE" : string.Join(",", pref)));
                if (partial.Length >= ts)
                {
                    var head = new byte[ts];
                    Buffer.BlockCopy(partial, 0, head, 0, (int)ts);
                    Console.Error.WriteLine($"  CRC32(first {ts} bytes) = 0x{RvtIO.Parsing.ElemTableRecover.DiagCrc32(head):X8}");
                    var tail2 = new byte[ts];
                    Buffer.BlockCopy(partial, partial.Length - (int)ts, tail2, 0, (int)ts);
                    Console.Error.WriteLine($"  CRC32(last  {ts} bytes) = 0x{RvtIO.Parsing.ElemTableRecover.DiagCrc32(tail2):X8}");
                }
            }
            string dumpDir = args.Contains("--dump-partial") ? Path.GetTempPath() : "";
            if (dumpDir.Length > 0)
            {
                string dp = System.IO.Path.Combine(dumpDir, System.IO.Path.GetFileNameWithoutExtension(rvtPath) + ".partial.bin");
                File.WriteAllBytes(dp, partial);
                Console.Error.WriteLine($"  wrote {dp}");
            }
        }
        Console.Error.WriteLine($"total {swTotal.Elapsed.TotalSeconds:F1}s");
        return 0;
    }

    // --class-census <out.json>: for every block classIdx (low16), collect up to N owner eids.
    private static int ClassCensus(string rvtPath, string classCensusPath)
    {
        using var rootCc = OpenMcdf.RootStorage.OpenRead(rvtPath);
        var psCc = rootCc.OpenStorage("Partitions");
        var peCc = psCc.EnumerateEntries().Where(e => e.Type == OpenMcdf.EntryType.Stream).OrderByDescending(e => e.Length).ToList();
        using var pstrCc = psCc.OpenStream(peCc[0].Name);
        var rawCc = new byte[pstrCc.Length];
        pstrCc.ReadExactly(rawCc);
        var dcCc = new RvtIO.Parsing.ChunkDecompressor();
        var cksCc = dcCc.DecompressAll(rawCc);
        var etCc = RvtIO.Parsing.ElemTableReader.Read(rootCc);
        var knownCc = new HashSet<uint>(etCc.Keys);
        var census = new Dictionary<ushort, List<uint>>();
        var counts = new Dictionary<ushort, int>();
        foreach (var chunk in cksCc)
        {
            int pos = 0;
            while (pos + 16 <= chunk.Length)
            {
                uint eid = BitConverter.ToUInt32(chunk, pos);
                if (pos + 12 <= chunk.Length)
                {
                    uint rSize = BitConverter.ToUInt32(chunk, pos + 4);
                    ushort magic = BitConverter.ToUInt16(chunk, pos + 8);
                    if ((magic == 0x053C || magic == 0x04F8) && eid > 0 && eid < 10_000_000
                        && rSize > 0 && rSize < 500_000 && pos + 12 + (int)rSize <= chunk.Length)
                    { pos += 12 + (int)rSize; continue; }
                }
                uint dataSize = BitConverter.ToUInt32(chunk, pos + 8);
                if (eid == 0) { pos += 4; continue; }
                if (dataSize == 0 || dataSize > 1_000_000) { pos++; continue; }
                if (pos + 16 + (int)dataSize > chunk.Length) break;
                if (!knownCc.Contains(eid)) { pos++; continue; }
                ushort cls = (ushort)(BitConverter.ToUInt32(chunk, pos + 12) & 0xFFFF);
                counts.TryGetValue(cls, out int c); counts[cls] = c + 1;
                if (!census.TryGetValue(cls, out var list)) census[cls] = list = new List<uint>();
                if (list.Count < 60) list.Add(eid);
                pos += 16 + (int)dataSize;
            }
        }
        var outObj = census.ToDictionary(kv => kv.Key.ToString(),
            kv => new { count = counts[kv.Key], eids = kv.Value });
        System.IO.File.WriteAllText(classCensusPath, System.Text.Json.JsonSerializer.Serialize(outObj));
        Console.Error.WriteLine($"class census: {census.Count} classIdx values -> {classCensusPath}");
        return 0;
    }

    // --tf-calibrate: run the generic schema deserializer over all blocks; report per-class
    // completeness and validate HVAC fields against the empirical extractor.
    private static int TfCalibrate(string rvtPath)
    {
        using var rootTf = OpenMcdf.RootStorage.OpenRead(rvtPath);
        var psTf = rootTf.OpenStorage("Partitions");
        var peTf = psTf.EnumerateEntries().Where(e => e.Type == OpenMcdf.EntryType.Stream).OrderByDescending(e => e.Length).ToList();
        using var pstrTf = psTf.OpenStream(peTf[0].Name);
        var rawTf = new byte[pstrTf.Length];
        pstrTf.ReadExactly(rawTf);
        var dcTf = new RvtIO.Parsing.ChunkDecompressor();
        var cksTf = dcTf.DecompressAll(rawTf);
        var etTf = RvtIO.Parsing.ElemTableReader.Read(rootTf);
        var knownTf = new HashSet<uint>(etTf.Keys);
        var stats = new Dictionary<ushort, (int tried, int complete, long fieldsSum)>();
        int hvacOk = 0, hvacBad = 0;
        foreach (var chunk in cksTf)
        {
            int pos = 0;
            while (pos + 16 <= chunk.Length)
            {
                uint eid = BitConverter.ToUInt32(chunk, pos);
                if (pos + 12 <= chunk.Length)
                {
                    uint rSize = BitConverter.ToUInt32(chunk, pos + 4);
                    ushort magic = BitConverter.ToUInt16(chunk, pos + 8);
                    if ((magic == 0x053C || magic == 0x04F8) && eid > 0 && eid < 10_000_000
                        && rSize > 0 && rSize < 500_000 && pos + 12 + (int)rSize <= chunk.Length)
                    { pos += 12 + (int)rSize; continue; }
                }
                uint dataSize = BitConverter.ToUInt32(chunk, pos + 8);
                if (eid == 0) { pos += 4; continue; }
                if (dataSize == 0 || dataSize > 1_000_000) { pos++; continue; }
                if (pos + 16 + (int)dataSize > chunk.Length) break;
                if (!knownTf.Contains(eid)) { pos++; continue; }
                ushort cls = BitConverter.ToUInt16(chunk, pos + 12);
                if (RvtIO.Parsing.TfBlockDeserializer.Schemas.ContainsKey(cls))
                {
                    var np = RvtIO.Parsing.TfBlockDeserializer.ExtractParams(chunk, pos, dataSize, cls);
                    stats.TryGetValue(cls, out var st);
                    stats[cls] = (st.tried + 1, st.complete + (np != null ? 1 : 0), st.fieldsSum + (np?.Count ?? 0));
                    if (np != null && np.Count > 0) hvacOk++;
                    else hvacBad++;
                }
                pos += 16 + (int)dataSize;
            }
        }
        Console.WriteLine($"blocks with native params: {hvacOk}, walked-no-params: {hvacBad}");
        Console.WriteLine($"classes attempted: {stats.Count}");
        var rows = stats.OrderByDescending(kv => kv.Value.tried).Take(25);
        foreach (var (cls, st) in rows)
        {
            string nm = RvtIO.Parsing.R22ClassDefs.Names.GetValueOrDefault(cls, "?");
            Console.WriteLine($"  0x{cls:X4} {nm,-30} tried={st.tried,7} complete={st.complete,7} avgFields={(double)st.fieldsSum / st.tried:F1}");
        }
        int totTried = stats.Sum(kv => kv.Value.tried), totCompl = stats.Sum(kv => kv.Value.complete);
        Console.WriteLine($"TOTAL blocks tried={totTried} complete={totCompl} ({100.0 * totCompl / Math.Max(1, totTried):F1}%)");
        return 0;
    }

    // --episode-check <id1,id2,...>: print ElemTableReader.LastEpisodeNumbers (the raw
    // ElemRec.History.CreationDate field read from ElemTable offset+8) for specific element ids.
    // The raw value still needs an unresolved per-document transform before it indexes EpisodeGuids.
    private static int EpisodeCheck(string rvtPath, string episodeCheckIds)
    {
        var targetEc = episodeCheckIds.Split(',').Select(s => uint.Parse(s.Trim())).ToList();
        using var rootEc = OpenMcdf.RootStorage.OpenRead(rvtPath);
        _ = RvtIO.Parsing.ElemTableReader.Read(rootEc);
        var epEc = RvtIO.Parsing.ElemTableReader.LastEpisodeNumbers;
        Console.WriteLine($"LastEpisodeNumbers: {epEc.Count} entries" + (epEc.Count > 0
            ? $", min={epEc.Values.Min()}, max={epEc.Values.Max()}" : ""));
        foreach (var id in targetEc)
            Console.WriteLine(epEc.TryGetValue(id, out int v) ? $"{id} -> {v}" : $"{id} -> MISSING");
        return 0;
    }

    // --test-decoders <gzFile>: run each decoder separately on a raw gzip member, report sizes
    private static int TestDecoders(string testDecodersFile)
    {
        var gz = System.IO.File.ReadAllBytes(testDecodersFile);
        try
        {
            using var ms = new System.IO.MemoryStream();
            using var gs = new System.IO.Compression.GZipStream(new System.IO.MemoryStream(gz), System.IO.Compression.CompressionMode.Decompress);
            gs.CopyTo(ms);
            Console.WriteLine($"GZipStream: OK {ms.Length} bytes");
            System.IO.File.WriteAllBytes(testDecodersFile + ".gzipstream", ms.ToArray());
        }
        catch (Exception ex) { Console.WriteLine($"GZipStream: FAIL {ex.GetType().Name}: {ex.Message}"); }
        try
        {
            // skip 10-byte gzip header (FLG=0 assumed), drop 8-byte trailer
            using var cs = new System.IO.MemoryStream(gz, 10, gz.Length - 10 - 8);
            using var ds = new RvtIO.DeflateManaged.Deflate64Stream(cs);
            using var output = new System.IO.MemoryStream();
            ds.CopyTo(output);
            Console.WriteLine($"Deflate64Stream: OK {output.Length} bytes");
            System.IO.File.WriteAllBytes(testDecodersFile + ".d64", output.ToArray());
        }
        catch (Exception ex) { Console.WriteLine($"Deflate64Stream: FAIL {ex.GetType().Name}: {ex.Message}"); }
        try
        {
            using var cs = new System.IO.MemoryStream(gz, 10, gz.Length - 10);
            using var ds = new RvtIO.DeflateManaged.Deflate64Stream(cs);
            using var output = new System.IO.MemoryStream();
            ds.CopyTo(output);
            Console.WriteLine($"Deflate64Stream(no-trailer-strip): OK {output.Length} bytes");
            System.IO.File.WriteAllBytes(testDecodersFile + ".d64nt", output.ToArray());
        }
        catch (Exception ex) { Console.WriteLine($"Deflate64Stream(no-trailer-strip): FAIL {ex.GetType().Name}: {ex.Message}"); }
        try
        {
            using var cs = new System.IO.MemoryStream(gz, 10, gz.Length - 10 - 8);
            using var ds = new ICSharpCode.SharpZipLib.Zip.Compression.Streams.InflaterInputStream(cs,
                new ICSharpCode.SharpZipLib.Zip.Compression.Inflater(noHeader: true));
            using var output = new System.IO.MemoryStream();
            ds.CopyTo(output);
            Console.WriteLine($"SharpZipLib: OK {output.Length} bytes");
            System.IO.File.WriteAllBytes(testDecodersFile + ".szl", output.ToArray());
        }
        catch (Exception ex) { Console.WriteLine($"SharpZipLib: FAIL {ex.GetType().Name}: {ex.Message}"); }
        uint expCrc = BitConverter.ToUInt32(gz, gz.Length - 8);
        uint expLen = BitConverter.ToUInt32(gz, gz.Length - 4);
        Console.WriteLine($"Trailer: CRC=0x{expCrc:X8} ISIZE={expLen}");
        return 0;
    }

    // --dump-chunks <dir>: write every decompressed partition chunk to <dir>/<stream>_<idx>.bin
    /// <summary>Builds a dump file path from a stream name read out of the parsed file: the name is reduced to one
    /// segment and the final path must stay inside the root folder.</summary>
    private static string SafeDumpFile(string rootFullPath, string streamName, string suffix)
    {
        var bare = new System.Text.StringBuilder(streamName.Length);
        foreach (char c in streamName)
            bare.Append(c == '/' || c == '\\' || Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
        string safe = bare.ToString().Trim();
        if (safe.Length == 0 || safe == "." || safe == "..") safe = "unnamed";
        string full = Path.GetFullPath(Path.Combine(rootFullPath, safe + suffix));
        if (!full.StartsWith(rootFullPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException($"stream name '{streamName}' moves the write outside '{rootFullPath}'");
        return full;
    }

    private static int DumpChunks(string rvtPath, string dumpChunksDir)
    {
        System.IO.Directory.CreateDirectory(dumpChunksDir);
        string dumpChunksRoot = Path.GetFullPath(dumpChunksDir);
        using var rootFsDc = OpenMcdf.RootStorage.OpenRead(rvtPath);
        var psDc = rootFsDc.OpenStorage("Partitions");
        var peDc = psDc.EnumerateEntries().Where(e => e.Type == OpenMcdf.EntryType.Stream).OrderByDescending(e => e.Length).ToList();
        var dcDc = new RvtIO.Parsing.ChunkDecompressor();
        int total = 0;
        foreach (var entry in peDc)
        {
            using var pstrDc = psDc.OpenStream(entry.Name);
            var rawDc = new byte[pstrDc.Length];
            pstrDc.ReadExactly(rawDc);
            var cksDc = dcDc.DecompressAll(rawDc);
            for (int ci = 0; ci < cksDc.Count; ci++)
            {
                System.IO.File.WriteAllBytes(SafeDumpFile(dumpChunksRoot, entry.Name, $"_{ci:D4}.bin"), cksDc[ci]);
                total++;
            }
        }
        Console.Error.WriteLine($"Wrote {total} chunks to {dumpChunksDir}");
        return 0;
    }

    // --name-scan: find element-name table entries across all partition chunks.
    // Pattern: [01 00 00 00][selfEid:4][FF x28][00 x0..8][01 00 00 00][targetId:4][nameLen:4][UTF-16 name][catId:4]
    private static int NameScan(string rvtPath)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        using var rootFsNs = OpenMcdf.RootStorage.OpenRead(rvtPath);
        var psNs = rootFsNs.OpenStorage("Partitions");
        var peNs = psNs.EnumerateEntries().Where(e => e.Type == OpenMcdf.EntryType.Stream).OrderByDescending(e => e.Length).ToList();
        var dcNs = new RvtIO.Parsing.ChunkDecompressor();
        int found = 0;
        foreach (var entry in peNs)
        {
            using var pstrNs = psNs.OpenStream(entry.Name);
            var rawNs = new byte[pstrNs.Length];
            pstrNs.ReadExactly(rawNs);
            var cksNs = dcNs.DecompressAll(rawNs);
            for (int ci = 0; ci < cksNs.Count; ci++)
            {
                var chunk = cksNs[ci];
                for (int pos = 8; pos + 28 <= chunk.Length; pos++)
                {
                    if (chunk[pos] != 0xFF) continue;
                    bool allFF = true;
                    for (int k = 1; k < 28; k++) if (chunk[pos + k] != 0xFF) { allFF = false; break; }
                    if (!allFF) continue;
                    if (pos + 28 < chunk.Length && chunk[pos + 28] == 0xFF) { continue; } // longer FF run: not ours
                    uint selfEid = BitConverter.ToUInt32(chunk, pos - 4);
                    int p = pos + 28;
                    int zeros = 0;
                    while (p < chunk.Length && chunk[p] == 0 && zeros < 9) { p++; zeros++; }
                    if (p + 12 > chunk.Length) continue;
                    if (BitConverter.ToUInt32(chunk, p) != 1) continue;
                    uint targetId = BitConverter.ToUInt32(chunk, p + 4);
                    int nameLen = BitConverter.ToInt32(chunk, p + 8);
                    if (targetId == 0 || targetId > 50_000_000) continue;
                    if (nameLen < 1 || nameLen > 500 || p + 12 + nameLen * 2 + 4 > chunk.Length) continue;
                    var sbN = new System.Text.StringBuilder();
                    bool ok = true;
                    for (int k = 0; k < nameLen; k++)
                    {
                        char c = (char)BitConverter.ToUInt16(chunk, p + 12 + k * 2);
                        if (c == 0 || (c < 0x20 && c != '\t')) { ok = false; break; }
                        sbN.Append(c);
                    }
                    if (!ok) continue;
                    int catId = BitConverter.ToInt32(chunk, p + 12 + nameLen * 2);
                    Console.WriteLine($"NAME str='{entry.Name}' chunk[{ci}] pos=0x{pos:X} self={selfEid} target={targetId} cat={catId} name=\"{sbN}\"");
                    found++;
                }
            }
        }
        Console.Error.WriteLine($"Total name entries: {found}");
        return 0;
    }

    // --typemap : test hypothesis that ElemTable typeId is a 0-based index into the
    // ordered Formats/Latest class-name list. Prints typeId histogram with resolved class names.
    private static int TypeMap(string rvtPath)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        using var rootTm = OpenMcdf.RootStorage.OpenRead(rvtPath);

        var fmtStor = rootTm.OpenStorage("Formats");
        var fmtEntry = fmtStor.EnumerateEntries()
            .Where(e => e.Type == OpenMcdf.EntryType.Stream)
            .OrderByDescending(e => e.Length).First();
        using var fmtStream = fmtStor.OpenStream(fmtEntry.Name);
        var fmtRaw = new byte[fmtStream.Length];
        fmtStream.ReadExactly(fmtRaw);
        var dcTm = new RvtIO.Parsing.ChunkDecompressor();
        var fmtChunks = dcTm.DecompressAll(fmtRaw);
        var classNames = RvtIO.Parsing.TfClassRegistry.ParseOrderedClassNames(fmtChunks[0]);
        Console.WriteLine($"Formats/Latest: {fmtChunks.Count} chunks, {classNames.Count} ordered class names");

        var elemTableTm = RvtIO.Parsing.ElemTableReader.Read(rootTm);
        Console.WriteLine($"ElemTable: {elemTableTm.Count} elements");
        var hist = new Dictionary<uint, int>();
        foreach (var tid in elemTableTm.Values) { hist.TryGetValue(tid, out int c); hist[tid] = c + 1; }

        string NameOf(uint tid) => tid < classNames.Count ? classNames[(int)tid] : $"<oob {tid}>";

        Console.WriteLine($"\n# typeId histogram (top 60 of {hist.Count} distinct), resolved as classNames[typeId]:");
        foreach (var (tid, cnt) in hist.OrderByDescending(kv => kv.Value).Take(60))
            Console.WriteLine($"  typeId {tid,4} -> {NameOf(tid),-34} ({cnt} elements)");

        uint[] probe = { 263584, 263597, 669552, 669553, 1757, 1049, 12344 };
        Console.WriteLine($"\n# Probe elements:");
        foreach (var e in probe)
            if (elemTableTm.TryGetValue(e, out var tid))
                Console.WriteLine($"  elem {e}: typeId {tid} -> {NameOf(tid)}");
        return 0;
    }

    private static int BlockMap(string rvtPath)
    {
        var readerCompact = new RvtFileReader(new RvtReaderOptions { DiagnosticOutput = Console.Error });
        var (_, stateCompact) = readerCompact.ReadWithState(rvtPath);
        var inv = stateCompact.BlockClassInventory;

        int totalBlocks = inv.Values.Sum(v => v.Count);
        long totalBytes = inv.Values.Sum(v => v.TotalBytes);
        Console.WriteLine($"# Block ClassIdx Inventory: {System.IO.Path.GetFileName(rvtPath)}");
        Console.WriteLine($"# {totalBlocks:N0} blocks ({totalBytes:N0} bytes), {inv.Count} distinct classIdx values");
        Console.WriteLine();

        var known = new Dictionary<uint, string>
        {
            [0x0702] = "User params (strings + doubles + ElementId BIPs)",
            [0x07D7] = "GRep geometry (R22)",
            [0x07D6] = "GRep geometry (R20)",
        };
        // Tf-serialized entries (0xFFFF high word) are keyed by their low 16 bits: classKey = classIdx & 0xFFFF

        // Filter: only classIdx with count >= 3 (singletons/pairs are scan noise)
        var filtered = inv.Where(kv => kv.Value.Count >= 3)
            .OrderByDescending(kv => kv.Value.TotalBytes)
            .ToList();
        int noiseClasses = inv.Count - filtered.Count;
        Console.WriteLine($"# Filtered: {filtered.Count} classIdx (excluded {noiseClasses} with count < 3)");
        Console.WriteLine();

        Console.WriteLine($"| classIdx | Count | TotalBytes | AvgSize | MinSize | MaxSize | Status | SampleElemIds | TopTypeIds |");
        Console.WriteLine($"|----------|-------|------------|---------|---------|---------|--------|---------------|------------|");
        foreach (var (ci, info) in filtered)
        {
            string status = known.TryGetValue(ci, out var desc) ? $"KNOWN: {desc}" : "UNKNOWN";
            string samples = string.Join(", ", info.SampleElemIds.Take(3));
            double avg = info.Count > 0 ? (double)info.TotalBytes / info.Count : 0;
            string topTypeIds = info.TypeIdCounts.Count == 0
                ? "-"
                : string.Join(", ", info.TypeIdCounts
                    .OrderByDescending(kv => kv.Value)
                    .Take(3)
                    .Select(kv => $"{kv.Key}({kv.Value})"));
            Console.WriteLine($"| 0x{ci:X4} | {info.Count:N0} | {info.TotalBytes:N0} | {avg:F0} | {info.MinSize} | {info.MaxSize} | {status} | {samples} | {topTypeIds} |");

            if (info.SampleData != null && status.StartsWith("UNKNOWN"))
            {
                var hex = string.Join(" ", info.SampleData.Select(b => b.ToString("X2")));
                Console.WriteLine($"|          |       |            |         |         |         | hex: {hex} | | |");
            }
        }

        return 0;
    }

    // --dump-storage <name> <outDir> : unpack a named storage into separate files
    private static int DumpStorage(string rvtPath, string dumpStorageName, string dumpStorageDir)
    {
        Directory.CreateDirectory(dumpStorageDir);
        using var rootDs = OpenMcdf.RootStorage.OpenRead(rvtPath);
        var storDs = rootDs.OpenStorage(dumpStorageName);
        var decompressorDs = new RvtIO.Parsing.ChunkDecompressor();
        int idx = 0;

        // The stream name is read from the CFB directory inside the .rvt, so it is data of the parsed file,
        // and such files come from outside. A name like "..\..\x" or an absolute path would move the write
        // outside the destination folder, so the name is reduced to one segment and the final
        // path is checked for leaving the root.
        string dumpRootDs = Path.GetFullPath(dumpStorageDir);
        string SafeDumpPath(string streamName, string suffix) => SafeDumpFile(dumpRootDs, streamName, suffix);
        foreach (var entry in storDs.EnumerateEntries().Where(e => e.Type == OpenMcdf.EntryType.Stream))
        {
            using var streamDs = storDs.OpenStream(entry.Name);
            var rawDs = new byte[streamDs.Length];
            streamDs.ReadExactly(rawDs);
            List<byte[]> chunksDs;
            try { chunksDs = decompressorDs.DecompressAll(rawDs); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{dumpStorageName}/{entry.Name}: could not be unpacked ({ex.Message}), writing the raw stream");
                File.WriteAllBytes(SafeDumpPath(entry.Name, ".raw.bin"), rawDs);
                continue;
            }
            foreach (var chunkDs in chunksDs)
            {
                var outPath = SafeDumpPath(entry.Name, $".{idx:D4}.bin");
                File.WriteAllBytes(outPath, chunkDs);
                Console.Error.WriteLine($"{outPath}: {chunkDs.Length:N0} bytes");
                idx++;
            }
        }
        return 0;
    }
}
