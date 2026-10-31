namespace RvtIO.Parsing;

/// <summary>
/// Desync-recovering inflater for G-tree (geometry) partition members, generalizing the ElemTable
/// recovery to the G-BLOB grammar.
/// </summary>
internal static class GBlockRecover
{
    private static readonly uint[] BlobFlags = { 0x80004, 0x88004, 0x88204, 0x1080004, 0x1088004 };

    private static bool IsBlobAt(byte[] a, int off)
    {
        if (BitConverter.ToInt32(a, off + 4) != 0) return false;
        uint fl = BitConverter.ToUInt32(a, off + 12);
        bool flOk = false;
        foreach (var f in BlobFlags) if (fl == f) { flOk = true; break; }
        if (!flOk) return false;
        int gs = BitConverter.ToInt32(a, off + 8);
        if (gs != -1 && (gs <= 0 || gs >= 0x1000000)) return false;
        int tag = BitConverter.ToInt32(a, off);
        return tag >= -1 && tag < 0x100000;
    }

    /// <summary>
    /// Recover a corrupt member. Returns output of EXACTLY isize bytes (without the dictionary
    /// prefix), or null when the grammar doesn't apply / no relock found.
    /// </summary>
    public static byte[]? TryRecover(byte[] raw, int st, int end, byte[] dict, int dictLen, uint isize)
        => TryRecover(raw, st, end, dict, dictLen, isize, out _);

    /// <summary>
    /// Start-relock for members whose FIRST deflate block header is corrupt (every standard decoder
    /// dies instantly; ~40 such members hold heavy-floor sketch lines and door instance blocks).
    /// </summary>
    public static byte[]? TryRecoverStart(byte[] raw, int st, int end, byte[] dict, int dictLen, uint isize)
    {
        try
        {
            int flg = raw[st + 3];
            int pos = st + 10;
            if ((flg & 0x04) != 0) { int xlen = raw[pos] | (raw[pos + 1] << 8); pos += 2 + xlen; }
            if ((flg & 0x08) != 0) { while (pos < end && raw[pos] != 0) pos++; pos++; }
            if ((flg & 0x10) != 0) { while (pos < end && raw[pos] != 0) pos++; pos++; }
            if ((flg & 0x02) != 0) pos += 2;

            var view = new byte[end - st];
            Array.Copy(raw, st, view, 0, end - st);
            long defStart = (long)(pos - st) * 8;
            long scanHi = Math.Min(defStart + 32768, (long)view.Length * 8 - 64);

            byte[]? best = null; long bestGap = long.MaxValue;
            object gate = new();
            Parallel.For(0, (int)((scanHi - defStart + 2047) / 2048), segIdx =>
            {
                long from = defStart + (long)segIdx * 2048, to = Math.Min(scanHi, from + 2048);
                var probe = new ElemTableRecover.ByteBuf(65536);
                for (long b = from; b < to; b++)
                {
                    var br = new ElemTableRecover.BitReader(view, b);
                    ElemTableRecover.Huff lit, dist;
                    try
                    {
                        br.Get(1);
                        if (br.Get(2) != 2) continue; // dynamic blocks only
                        ElemTableRecover.ReadDynHeader(br, out lit, out dist);
                    }
                    catch (ElemTableRecover.DesyncException) { continue; }

                    probe.Count = 0;
                    if (dictLen > 0) probe.AddRange(dict, dict.Length - dictLen, dictLen);
                    int seed = probe.Count;
                    try { DecodeSymbols(br, lit, dist, probe, 2048); }
                    catch (ElemTableRecover.DesyncException) { continue; }
                    if (probe.Count - seed < 512) continue;
                    if (!ProbeHasGrammar(probe, seed)) continue;

                    // Extend to the member end and check the ISIZE fit.
                    var tail = ExtendFromBlockStart(view, b, dict, dictLen);
                    if (tail == null) continue;
                    long gap = (long)isize - tail.Length;
                    if (gap < 0 || gap > 131072) continue;
                    lock (gate) if (gap < bestGap)
                    {
                        bestGap = gap;
                        var res = new byte[isize];
                        Buffer.BlockCopy(tail, 0, res, (int)gap, tail.Length);
                        best = res;
                    }
                }
            });
            return best;
        }
        catch (Exception ex) when (MalformedInput.Is(ex)) { return null; } // not a recoverable gzip member
    }

    private static bool ProbeHasGrammar(ElemTableRecover.ByteBuf buf, int seed)
    {
        int blobs = 0;
        for (int off = seed; off + 16 <= buf.Count; off++)
            if (IsBlobAt(buf.Arr, off)) { if (++blobs >= 3) return true; off += 15; }
        int items = 0;
        for (int off = seed; off + 16 <= buf.Count; off++)
        {
            uint eid = buf.U32(off);
            if (eid == 0 || eid >= 50_000_000) continue;
            ushort magic = (ushort)(buf.Arr[off + 8] | (buf.Arr[off + 9] << 8));
            if ((magic == 0x053C || magic == 0x04F8) && buf.U32(off + 4) < 500_000)
            { if (++items >= 2) return true; off += 11; }
        }
        // Grammar 3a: UTF-16LE text run >= 10 chars (Latin printable [xx 00] or Cyrillic [10-4F
        // 04]) - dense in name tables and string params.
        for (int off = seed; off + 20 < buf.Count; off++)
        {
            int run = 0, p = off;
            while (p + 1 < buf.Count)
            {
                byte lo = buf.Arr[p], hi = buf.Arr[p + 1];
                bool ok = (hi == 0x00 && lo >= 0x20 && lo < 0x7F) || (hi == 0x04 && lo >= 0x10 && lo <= 0x5F);
                if (!ok) break;
                run++; p += 2;
            }
            if (run >= 10) return true;
            if (run > 0) off = p;
        }
        // Grammar 3b: param-store chain - consecutive (f64, i32) entries whose i32 sits in the
        // built-in-param id band (-1.2M..-0.9M) - a tight, distinctive range.
        for (int off = seed; off + 36 <= buf.Count; off++)
        {
            int good = 0;
            for (int k = 0; k < 3; k++)
            {
                double d = BitConverter.ToDouble(buf.Arr, off + 12 * k);
                int bip = BitConverter.ToInt32(buf.Arr, off + 12 * k + 8);
                if (double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > 1e12) break;
                if (bip > -900_000 || bip < -1_200_000) break;
                good++;
            }
            if (good >= 3) return true;
        }
        return false;
    }

    /// <summary>Decode from a block start bit (bfinal+btype+header included) to BFINAL.</summary>
    private static byte[]? ExtendFromBlockStart(byte[] view, long bit, byte[] dict, int dictLen)
    {
        var o = new ElemTableRecover.ByteBuf(1 << 18);
        if (dictLen > 0) o.AddRange(dict, dict.Length - dictLen, dictLen);
        int seed = o.Count;
        var br = new ElemTableRecover.BitReader(view, bit);
        try
        {
            while (br.BitPos + 3 <= br.N)
            {
                uint bfinal = br.Get(1); uint btype = br.Get(2);
                if (btype == 0)
                {
                    br.Align();
                    int bp = (int)(br.BitPos >> 3);
                    if (bp + 4 > view.Length) return null;
                    int ln = view[bp] | (view[bp + 1] << 8);
                    int nlen = view[bp + 2] | (view[bp + 3] << 8);
                    if (ln != ((~nlen) & 0xFFFF)) return null;
                    bp += 4;
                    if (bp + ln > view.Length) return null;
                    o.AddRange(view, bp, ln);
                    br.BitPos = (long)(bp + ln) * 8;
                }
                else if (btype == 3) return null;
                else
                {
                    ElemTableRecover.Huff l2, d2;
                    if (btype == 1) { l2 = ElemTableRecover.FixedLit; d2 = ElemTableRecover.FixedDist; }
                    else ElemTableRecover.ReadDynHeader(br, out l2, out d2);
                    DecodeSymbols(br, l2, d2, o, int.MaxValue);
                }
                if (bfinal != 0) break;
            }
        }
        catch (ElemTableRecover.DesyncException) { return null; }
        var tail = new byte[o.Count - seed];
        Buffer.BlockCopy(o.Arr, seed, tail, 0, tail.Length);
        return tail;
    }

    public static byte[]? TryRecover(byte[] raw, int st, int end, byte[] dict, int dictLen, uint isize, out string reason)
    {
        try { return TryRecoverCore(raw, st, end, dict, dictLen, isize, out reason); }
        catch (Exception ex) { reason = "exc:" + ex.GetType().Name; return null; }
    }

    private static byte[]? TryRecoverCore(byte[] raw, int st, int end, byte[] dict, int dictLen, uint isize, out string reason)
    {
        reason = "ok";
        // Deflate bitstream position past the gzip header.
        int flg = raw[st + 3];
        int pos = st + 10;
        if ((flg & 0x04) != 0) { int xlen = raw[pos] | (raw[pos + 1] << 8); pos += 2 + xlen; }
        if ((flg & 0x08) != 0) { while (pos < end && raw[pos] != 0) pos++; pos++; }
        if ((flg & 0x10) != 0) { while (pos < end && raw[pos] != 0) pos++; pos++; }
        if ((flg & 0x02) != 0) pos += 2;

        // The relock decoder reads bits up to `end` only - clamp the buffer view.
        var view = new byte[end - st];
        Array.Copy(raw, st, view, 0, end - st);
        long deflateStart = (long)(pos - st) * 8;

        var outp = new ElemTableRecover.ByteBuf(1 << 20);
        if (dictLen > 0) outp.AddRange(dict, dict.Length - dictLen, dictLen);
        int seed = outp.Count;

        var br = new ElemTableRecover.BitReader(view, deflateStart);
        var blocks = new List<(long BitStart, long BitEnd, int OutStart, int OutEnd,
            ElemTableRecover.Huff? Lit, ElemTableRecover.Huff? Dist)>();
        bool final = false;
        while (!final && br.BitPos + 3 <= br.N)
        {
            long bs = br.BitPos; int os = outp.Count;
            ElemTableRecover.Huff? lit = null, dist = null;
            try
            {
                uint bfinal = br.Get(1); uint btype = br.Get(2);
                if (btype == 0)
                {
                    br.Align();
                    int bp = (int)(br.BitPos >> 3);
                    if (bp + 4 > view.Length) break;
                    int ln = view[bp] | (view[bp + 1] << 8);
                    int nlen = view[bp + 2] | (view[bp + 3] << 8);
                    if (ln != ((~nlen) & 0xFFFF)) break;
                    bp += 4;
                    if (bp + ln > view.Length) break;
                    outp.AddRange(view, bp, ln);
                    br.BitPos = (long)(bp + ln) * 8;
                }
                else if (btype == 3) break;
                else
                {
                    if (btype == 1) { lit = ElemTableRecover.FixedLit; dist = ElemTableRecover.FixedDist; }
                    else ElemTableRecover.ReadDynHeader(br, out lit, out dist);
                    DecodeSymbols(br, lit!, dist!, outp, int.MaxValue);
                }
                final = bfinal != 0;
            }
            catch (ElemTableRecover.DesyncException) { break; }
            blocks.Add((bs, br.BitPos, os, outp.Count, lit, dist));
        }
        if (blocks.Count == 0) { reason = "no-blocks"; return null; }

        // Grammar 1: G-BLOB cadence (geometry members).
        int death = -1;
        bool recordGrammar = false;
        {
            var blobPos = new List<int>();
            for (int off = seed; off + 16 <= outp.Count; off++)
                if (IsBlobAt(outp.Arr, off)) blobPos.Add(off);
            if (blobPos.Count >= 8)
            {
                long cadSum = 0;
                for (int i = 1; i < blobPos.Count; i++) cadSum += blobPos[i] - blobPos[i - 1];
                if (cadSum / (blobPos.Count - 1) <= 2048 && outp.Count - blobPos[^1] >= 4096)
                    death = blobPos[^1] + 16;
            }
        }
        // Grammar 2: TF-STREAM item chain (record/block members - parameter data).
        if (death < 0 && FindRecordDeath(outp, seed, out int rd))
        {
            death = rd;
            recordGrammar = true;
        }
        if (death < 0) { reason = "no-grammar"; return null; }
        int ci = blocks.FindIndex(b => b.OutStart <= death && death <= b.OutEnd);
        if (ci < 0 || blocks[ci].Lit == null) { reason = (recordGrammar ? "r-" : "b-") + "no-corrupt-block"; return null; }
        var cb = blocks[ci];

        // The corrupt bit lies at/after the position where the block had produced the last valid
        // output - find that bit by re-decoding the block up to `death`.
        long deathBit = cb.BitStart;
        {
            var br2 = new ElemTableRecover.BitReader(view, cb.BitStart);
            var tmp = new ElemTableRecover.ByteBuf(outp.Count);
            tmp.AddRange(outp.Arr, 0, cb.OutStart);
            try
            {
                br2.Get(1); uint bt2 = br2.Get(2);
                ElemTableRecover.Huff l2 = cb.Lit!, d2 = cb.Dist!;
                if (bt2 == 2) ElemTableRecover.ReadDynHeader(br2, out l2, out d2);
                while (tmp.Count < death)
                {
                    deathBit = br2.BitPos;
                    int sym = l2.Decode(br2);
                    if (sym < 256) tmp.Add((byte)sym);
                    else if (sym == 256) break;
                    else
                    {
                        int i = sym - 257;
                        int length = ElemTableRecover.LenBase[i] + (int)br2.Get(ElemTableRecover.LenExtra[i]);
                        int ds = d2.Decode(br2);
                        int d = ElemTableRecover.DistBase[ds] + (int)br2.Get(ElemTableRecover.DistExtra[ds]);
                        int sp = tmp.Count - d;
                        for (int k = 0; k < length; k++) tmp.Add(tmp.Arr[sp + k]);
                    }
                }
            }
            catch (ElemTableRecover.DesyncException) { } // Expected: the stream desyncs at the probe end; keep the output so far
        }

        // Window = truncated valid prefix.
        int prefixLen = death;
        long scanLo = Math.Max(cb.BitStart + 24, deathBit - 2048);
        long scanHi = cb.BitEnd;
        var survivors = new List<long>();
        object gate = new();
        Parallel.For(0, (int)((scanHi - scanLo + 4095) / 4096), seg =>
        {
            var probeBuf = new ElemTableRecover.ByteBuf(4096);
            long from = scanLo + (long)seg * 4096, to = Math.Min(scanHi, from + 4096);
            var local = new List<long>();
            for (long b = from; b < to; b++)
            {
                probeBuf.Count = 0;
                if (ProbeDecode(view, cb.Lit!, cb.Dist!, outp, prefixLen, b, 96, probeBuf)
                    && (recordGrammar ? HasRecordHeader(probeBuf) : HasBlobSuffix(probeBuf)))
                    local.Add(b);
            }
            if (local.Count > 0) lock (gate) survivors.AddRange(local);
        });
        if (survivors.Count == 0 || survivors.Count > 4000)
        {
            // >4000 survivors = the pin can't discriminate (dense grammar matches wrong streams
            // too): proceeding would replace partially usable corrupt bytes with wrong
            // relocks and lose geometry blocks. Keep the original bytes.
            reason = (recordGrammar ? "r-" : "b-") + (survivors.Count == 0 ? "no-survivors" : "too-many-survivors");
            return null;
        }
        survivors.Sort();

        var unique = new List<long>();
        var seen = new HashSet<string>();
        var pb = new ElemTableRecover.ByteBuf(4096);
        foreach (var b in survivors)
        {
            pb.Count = 0;
            if (!ProbeDecode(view, cb.Lit!, cb.Dist!, outp, prefixLen, b, 48, pb)) continue;
            var key = Convert.ToBase64String(pb.Arr, 0, Math.Min(pb.Count, 48));
            if (seen.Add(key)) unique.Add(b);
        }
        byte[]? best = null; long bestGap = long.MaxValue;
        object cGate = new();
        Parallel.ForEach(unique, b =>
        {
            var tail = ExtendToEnd(view, cb.Lit!, cb.Dist!, outp, prefixLen, b);
            if (tail == null) return;
            long gap = (long)isize - (prefixLen - seed) - tail.Length;
            if (gap < 0 || gap > 32768) return;
            lock (cGate) if (gap < bestGap) { bestGap = gap; best = tail; }
        });
        if (best == null) { reason = (recordGrammar ? "r-" : "b-") + "no-gap-fit"; return null; }

        var res = new byte[isize];
        Buffer.BlockCopy(outp.Arr, seed, res, 0, prefixLen - seed);
        Buffer.BlockCopy(best, 0, res, (int)(prefixLen - seed + bestGap), best.Length);
        return res;
    }

    private static void DecodeSymbols(ElemTableRecover.BitReader br, ElemTableRecover.Huff lit,
        ElemTableRecover.Huff dist, ElemTableRecover.ByteBuf outp, int maxBytes)
    {
        int produced = 0;
        while (produced < maxBytes)
        {
            int sym = lit.Decode(br);
            if (sym < 256) { outp.Add((byte)sym); produced++; }
            else if (sym == 256) return;
            else
            {
                int i = sym - 257;
                if (i >= ElemTableRecover.LenBase.Length) ElemTableRecover.Bail();
                int length = ElemTableRecover.LenBase[i] + (int)br.Get(ElemTableRecover.LenExtra[i]);
                int ds = dist.Decode(br);
                if (ds >= ElemTableRecover.DistBase.Length) ElemTableRecover.Bail();
                int d = ElemTableRecover.DistBase[ds] + (int)br.Get(ElemTableRecover.DistExtra[ds]);
                if (d > outp.Count) ElemTableRecover.Bail();
                int st = outp.Count - d;
                for (int k = 0; k < length; k++) outp.Add(outp.Arr[st + k]);
                produced += length;
            }
        }
    }

    /// <summary>
    /// Decode up to maxBytes from `bit` using the given tables, with the window =
    /// outp[0..windowLen). Output lands in `res`. False on any decode error.
    /// </summary>
    private static bool ProbeDecode(byte[] view, ElemTableRecover.Huff lit, ElemTableRecover.Huff dist,
        ElemTableRecover.ByteBuf window, int windowLen, long bit, int maxBytes, ElemTableRecover.ByteBuf res)
    {
        var br = new ElemTableRecover.BitReader(view, bit);
        try
        {
            int produced = 0;
            while (produced < maxBytes)
            {
                int sym = lit.Decode(br);
                if (sym < 256) { res.Add((byte)sym); produced++; }
                else if (sym == 256) return false;
                else
                {
                    int i = sym - 257;
                    if (i >= ElemTableRecover.LenBase.Length) return false;
                    int length = ElemTableRecover.LenBase[i] + (int)br.Get(ElemTableRecover.LenExtra[i]);
                    int ds = dist.Decode(br);
                    if (ds >= ElemTableRecover.DistBase.Length) return false;
                    int d = ElemTableRecover.DistBase[ds] + (int)br.Get(ElemTableRecover.DistExtra[ds]);
                    if (d > windowLen + res.Count) return false;
                    for (int k = 0; k < length; k++)
                    {
                        int rel = res.Count - d;
                        res.Add(rel >= 0 ? res.Arr[rel] : window.Arr[windowLen + rel]);
                    }
                    produced += length;
                }
            }
            return true;
        }
        catch (ElemTableRecover.DesyncException) { return false; }
    }

    /// <summary>
    /// TF-stream item chain death: the output must contain a run of validated [eid][size][magic
    /// 0x053C/0x04F8] records / [eid][hash][ds][classIdx] blocks, then go item-silent for the rest.
    /// death = end of the last chained item.
    /// </summary>
    private static bool FindRecordDeath(ElemTableRecover.ByteBuf outp, int seed, out int death)
    {
        death = -1;
        int ItemSize(int off)
        {
            if (off + 16 > outp.Count) return -1;
            uint eid = outp.U32(off);
            if (eid == 0 || eid >= 50_000_000) return -1;
            ushort magic = (ushort)(outp.Arr[off + 8] | (outp.Arr[off + 9] << 8));
            uint bs = outp.U32(off + 4);
            if ((magic == 0x053C || magic == 0x04F8) && bs < 500_000) return 12 + (int)bs;
            uint ds = outp.U32(off + 8);
            ushort ci = (ushort)(outp.Arr[off + 12] | (outp.Arr[off + 13] << 8));
            if (ds < 1_000_000 && TfBlockDeserializer.Schemas.ContainsKey(ci)) return 16 + (int)ds;
            return -1;
        }
        // Find the chain start: a member's output usually begins MID-item, so scan for the first
        // double-validated header, then walk the chain greedily.
        int hits = 0, lastEnd = -1, off2 = seed;
        while (off2 + 16 <= outp.Count)
        {
            int sz = ItemSize(off2);
            if (sz > 0)
            {
                int nxt = off2 + sz;
                if (nxt >= outp.Count - 16 || ItemSize(nxt) > 0)
                {
                    hits++; lastEnd = Math.Min(nxt, outp.Count);
                    off2 = nxt;
                    continue;
                }
            }
            off2++;
        }
        if (hits < 8 || lastEnd < 0) return false;
        if (outp.Count - lastEnd < 4096) return false;
        death = lastEnd;
        return true;
    }

    private static bool HasRecordHeader(ElemTableRecover.ByteBuf buf)
    {
        int lim = Math.Min(buf.Count - 16, 80);
        for (int off = 0; off <= lim; off++)
        {
            uint eid = buf.U32(off);
            if (eid == 0 || eid >= 50_000_000) continue;
            ushort magic = (ushort)(buf.Arr[off + 8] | (buf.Arr[off + 9] << 8));
            if (magic == 0x053C || magic == 0x04F8)
            {
                if (buf.U32(off + 4) < 500_000) return true;
                continue;
            }
            uint ds = buf.U32(off + 8);
            ushort ci = (ushort)(buf.Arr[off + 12] | (buf.Arr[off + 13] << 8));
            if (ds < 1_000_000 && TfBlockDeserializer.Schemas.ContainsKey(ci)) return true;
        }
        return false;
    }

    private static bool HasBlobSuffix(ElemTableRecover.ByteBuf buf)
    {
        int lim = Math.Min(buf.Count - 12, 80);
        for (int off = 0; off <= lim; off++)
        {
            if (BitConverter.ToInt32(buf.Arr, off) != 0) continue;
            uint fl = BitConverter.ToUInt32(buf.Arr, off + 8);
            bool flOk = false;
            foreach (var f in BlobFlags) if (fl == f) { flOk = true; break; }
            if (!flOk) continue;
            int gs = BitConverter.ToInt32(buf.Arr, off + 4);
            if (gs == -1 || (gs > 0 && gs < 0x1000000)) return true;
        }
        return false;
    }

    /// <summary>
    /// Decode from `bit` (corrupt block's tables) to that block's EOB, then run the remaining
    /// blocks to BFINAL. Returns the tail bytes, or null on error.
    /// </summary>
    private static byte[]? ExtendToEnd(byte[] view, ElemTableRecover.Huff lit, ElemTableRecover.Huff dist,
        ElemTableRecover.ByteBuf window, int windowLen, long bit)
    {
        var o = new ElemTableRecover.ByteBuf(1 << 18);
        o.AddRange(window.Arr, 0, windowLen);
        var br = new ElemTableRecover.BitReader(view, bit);
        try
        {
            DecodeSymbols(br, lit, dist, o, int.MaxValue);
            while (br.BitPos + 3 <= br.N)
            {
                uint bfinal = br.Get(1); uint btype = br.Get(2);
                if (btype == 0)
                {
                    br.Align();
                    int bp = (int)(br.BitPos >> 3);
                    if (bp + 4 > view.Length) return null;
                    int ln = view[bp] | (view[bp + 1] << 8);
                    int nlen = view[bp + 2] | (view[bp + 3] << 8);
                    if (ln != ((~nlen) & 0xFFFF)) return null;
                    bp += 4;
                    if (bp + ln > view.Length) return null;
                    o.AddRange(view, bp, ln);
                    br.BitPos = (long)(bp + ln) * 8;
                }
                else if (btype == 3) return null;
                else
                {
                    ElemTableRecover.Huff l2, d2;
                    if (btype == 1) { l2 = ElemTableRecover.FixedLit; d2 = ElemTableRecover.FixedDist; }
                    else ElemTableRecover.ReadDynHeader(br, out l2, out d2);
                    DecodeSymbols(br, l2, d2, o, int.MaxValue);
                }
                if (bfinal != 0) break;
            }
        }
        catch (ElemTableRecover.DesyncException) { return null; }
        var tail = new byte[o.Count - windowLen];
        Buffer.BlockCopy(o.Arr, windowLen, tail, 0, tail.Length);
        return tail;
    }
}
