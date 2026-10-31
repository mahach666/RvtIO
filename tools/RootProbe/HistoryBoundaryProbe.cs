// Diagnostic only. Enumerate every supported skipped-history boundary without
// preferring a layout or an earlier tail. No material/API semantics are inferred.
internal static class HistoryBoundaryProbe
{
    internal sealed record Candidate(int FaceEnd, int CurveSize, int EdgeEnd,
        int TailStart, string Layout);

    // Grammar hypothesis for CompoundStructure.m_segRefFaceKeys; kept in this tool until the key semantics are settled.
    internal static bool TryTripletContainerEnd(byte[] bytes, int start, int end, out int fieldEnd)
    {
        fieldEnd = -1;
        if (start < 0 || end < 0 || end > bytes.Length || start > end - 4) return false;
        uint count = BitConverter.ToUInt32(bytes, start);
        if (count == uint.MaxValue) { fieldEnd = start + 4; return true; }
        long finish = (long)start + 4 + count * 12L;
        if (count > 100_000 || finish > end) return false;
        fieldEnd = (int)finish;
        return true;
    }

    internal static List<Candidate> VertCompoundStepCandidates(byte[] bytes, int start, int end)
        => StepCandidates(bytes, start, end, allowCountedEmptyFaceCurves: false);

    // Stair layout steps also persist the schema reader's current 24-byte curve
    // records after an empty face table. Enumerate both; never choose an earlier
    // candidate, and the caller must still require one shared complete tail.
    internal static List<Candidate> StairStepCandidates(byte[] bytes, int start, int end)
        => StepCandidates(bytes, start, end, allowCountedEmptyFaceCurves: true);

    private static List<Candidate> StepCandidates(byte[] bytes, int start, int end,
        bool allowCountedEmptyFaceCurves)
    {
        var result = new List<Candidate>();
        if (start < 0 || end < 0 || end > bytes.Length || start > end - 4) return result;
        uint first = U32(start);
        var faces = new List<(int End, int CurveSize, string Layout)>();
        if (first is 0 or uint.MaxValue)
        {
            // Existing empty-face grammar uses current 20-byte curve records.
            faces.Add((start + 4, 20, "empty-face"));
            if (allowCountedEmptyFaceCurves)
                faces.Add((start + 4, 24, "empty-face-counted-curves"));
        }
        else
        {
            int counted = CountEnd(start + 4, first, 16);
            if (counted >= 0) faces.Add((counted, 24, "counted-face"));
            int prefixed = TerminatedEnd(start + 4, 16);
            if (prefixed >= 0) faces.Add((prefixed, 20, "prefixed-terminated-face"));
            int unprefixed = TerminatedEnd(start, 16);
            if (unprefixed >= 0) faces.Add((unprefixed, 20, "terminated-face"));
        }
        foreach (var face in faces)
        {
            if (face.End + 4 > end) continue;
            uint count = U32(face.End);
            int curveEnd = count == uint.MaxValue ? face.End + 4
                : face.CurveSize == 24 ? CountEnd(face.End + 4, count, 24)
                : RawCountEnd(face.End + 4, count, 20);
            if (curveEnd < 0 || curveEnd + 4 > end) continue;
            foreach (var edge in EdgeEnds(curveEnd))
                foreach (var reverse in ReverseEnds(edge.End))
                    if (IsTail(reverse.End))
                        result.Add(new(face.End, face.CurveSize, edge.End,
                            reverse.End, face.Layout + "/" + edge.Layout + "/" + reverse.Layout));
        }
        return result;

        uint U32(int p) => BitConverter.ToUInt32(bytes, p);

        int RawCountEnd(int p, uint count, int size)
        {
            long finish = (long)p + count * (long)size;
            return p >= 0 && count <= 100_000 && finish <= end ? (int)finish : -1;
        }

        int CountEnd(int p, uint count, int size)
        {
            int finish = RawCountEnd(p, count, size);
            if (finish < 0) return -1;
            var ids = new HashSet<int>();
            for (uint i = 0; i < count; i++)
            {
                int id = BitConverter.ToInt32(bytes, p + (int)i * size);
                if (id < 0 || !ids.Add(id)) return -1;
            }
            return finish;
        }

        int TerminatedEnd(int p, int size)
        {
            var ids = new HashSet<int>();
            for (int i = 0; i < 100_000 && p >= 0 && p + size <= end; i++, p += size)
            {
                int id = BitConverter.ToInt32(bytes, p + size - 4);
                if (id == 0) return p + size;
                if (id < 0 || !ids.Add(id)) return -1;
            }
            return -1;
        }

        IEnumerable<(int End, string Layout)> EdgeEnds(int p)
        {
            if (p < 0 || p > end - 4) yield break;
            uint value = U32(p);
            if (value is 0 or uint.MaxValue)
            {
                yield return (p + 4, "empty-edge");
                yield break;
            }
            int counted = CountEnd(p + 4, value, 20);
            if (counted >= 0) yield return (counted, "counted-edge");
            int compact = CountEnd(p + 4, value, 16);
            if (compact >= 0 && compact + 4 <= end && U32(compact) == 0)
                yield return (compact + 4, "compact-count-terminated-edge");
            int terminated = TerminatedEnd(p + 4, 20);
            if (terminated >= 0) yield return (terminated, "terminated-edge");
        }

        IEnumerable<(int End, string Layout)> ReverseEnds(int p)
        {
            yield return (p, "omitted-reverse");
            if (p + 4 > end) yield break;
            uint value = U32(p);
            if (value is 0 or uint.MaxValue)
                yield return (p + 4, "empty-reverse");
            else if (value <= 100_000)
            {
                if (p + 12 <= end && U32(p + 4) == 0)
                {
                    int nested = CountEnd(p + 12, U32(p + 8), 20);
                    if (nested >= 0) yield return (nested, "high-water-count-reverse");
                }
                int counted = CountEnd(p + 4, value, 20);
                if (counted >= 0)
                {
                    yield return (counted, "counted-reverse");
                    if (counted + 4 <= end && U32(counted) == 0)
                        yield return (counted + 4, "count-terminated-reverse");
                }
            }
            int terminated = TerminatedEnd(p + 4, 20);
            if (terminated >= 0) yield return (terminated, "terminated-reverse");
        }

        bool IsTail(int p)
            => p >= 0 && p + 24 <= end && U32(p + 12) == 2
                && double.IsFinite(BitConverter.ToDouble(bytes, p + 16));
    }
}
