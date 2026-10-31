namespace RvtIO.Parsing;

// Stair layout read from the file's own fields: named pairs and counted histories.
// The values alone do not give the API style/path/order; the caller validates the graph.
internal static class StairGraphLayout
{
    internal sealed record Entry(double Parameter, int First, int Second);
    internal sealed record Pointer(uint Handle, ushort? ClassId);
    internal sealed record CurveData(int Key, int Id, int CurveIndex, int AuxiliaryIndex);
    internal sealed record CurveChain(CurveData[] Records, Pointer[] Curves);
    internal sealed record OrderedRiser(double LeftParameter, double RightParameter,
        double LeftAngle, double RightAngle, int CurveId, int LeftIntersection,
        int RightIntersection, int SpecifiedIndex, Pointer RiserCurve, Pointer MultiSegmentRiser);
    internal sealed record Layout(Entry[][] Intersections, CurveChain[] Boundaries,
        CurveChain[] Risers, int[] SortedRiserIndices, OrderedRiser[] OrderedRisers, byte LeadingIsLeft);
    internal sealed record AffectedTags(int ElementId, int[] Tags);

    // Inline pair/wrapper layout of the 679 -> 680 classes; the descriptor omits both refs.
    // Values and order are kept as stored.
    internal static bool TryAffectedTags(byte[] bytes, int start, int end,
        out int finish, out AffectedTags[] entries)
    {
        finish = -1; entries = [];
        if (start < 0 || end < 0 || end > bytes.Length || start > end - 4) return false;
        int pos = start;
        uint count = BitConverter.ToUInt32(bytes, pos); pos += 4;
        if (count == uint.MaxValue) count = 0;
        if (count > 100_000 || count * 8L > end - pos) return false;
        var result = new AffectedTags[(int)count];
        for (int i = 0; i < result.Length; i++)
        {
            if (pos > end - 8) return false;
            int id = BitConverter.ToInt32(bytes, pos);
            uint length = BitConverter.ToUInt32(bytes, pos + 4); pos += 8;
            if (length == uint.MaxValue) length = 0;
            if (length > 100_000 || length * 4L > end - pos) return false;
            int[] tags = new int[(int)length];
            for (int j = 0; j < tags.Length; j++, pos += 4) tags[j] = BitConverter.ToInt32(bytes, pos);
            result[i] = new(id, tags);
        }
        finish = pos; entries = result;
        return true;
    }

    // Separate grammar for the counted 98/100/102 records, including the reverse 102 table.
    // It is chosen explicitly and is not a fallback for ambiguous legacy histories; callers
    // validate the bounded FIFO scope and framing.
    internal static bool TryCurrentHistoryEnd(byte[] bytes, int start, int end, out int finish)
    {
        finish = -1;
        if (start < 0 || end < 0 || end > bytes.Length || start > end - 4) return false;
        int pos = start;
        foreach (int recordSize in new[] { 16, 24, 20, 20 })
        {
            if (pos > end - 4) return false;
            uint count = BitConverter.ToUInt32(bytes, pos); pos += 4;
            if (count == uint.MaxValue) count = 0;
            if (count > 100_000 || count * (long)recordSize > end - pos) return false;
            var ids = new HashSet<int>();
            for (uint i = 0; i < count; i++, pos += recordSize)
            {
                int id = BitConverter.ToInt32(bytes, pos);
                if (id < 0 || !ids.Add(id)) return false;
            }
        }
        if (pos > end - 16 || BitConverter.ToUInt32(bytes, pos + 12) != 2) return false;
        finish = pos;
        return true;
    }

    // Inline StairsRunLayoutInfo (class 3688) layout of the m_compatibleCrvInfo field.
    // CurveChain.curveElems includes the SortedCurveMap records and the owned curve factory array.
    // No offsets are searched and no run style/path/order is assigned to the values.
    internal static bool TryLayoutInfo(byte[] bytes, int start, int end,
        out int finish, out Layout? layout, out (ushort ClassId, uint Handle)[] deferred)
    {
        finish = -1; layout = null; deferred = [];
        if (!TryCompatibleCurveInfo(bytes, start, end, out int pos, out var intersections)) return false;
        var pointers = new List<(ushort, uint)>();
        try
        {
            CurveChain[] boundaries = Chains(), risers = Chains();
            int[] sorted = new int[Count(4)];
            for (int i = 0; i < sorted.Length; i++) sorted[i] = I32();
            var ordered = new OrderedRiser[Count(56)];
            for (int i = 0; i < ordered.Length; i++)
                ordered[i] = new(F64(), F64(), F64(), F64(), I32(), I32(), I32(), I32(), Ptr(), Ptr());
            Need(1);
            byte leading = bytes[pos++];
            if (leading > 1) return false;
            finish = pos;
            layout = new(intersections, boundaries, risers, sorted, ordered, leading);
            deferred = pointers.ToArray();
            return true;
        }
        catch (InvalidDataException) { return false; }

        void Need(int length)
        {
            if (pos > end - length) throw new InvalidDataException("Truncated stair layout");
        }
        uint U32() { Need(4); uint value = BitConverter.ToUInt32(bytes, pos); pos += 4; return value; }
        int I32() => unchecked((int)U32());
        double F64()
        {
            Need(8); double value = BitConverter.ToDouble(bytes, pos); pos += 8;
            if (!double.IsFinite(value)) throw new InvalidDataException("Nonfinite stair layout value");
            return value;
        }
        int Count(int minimumRecordSize)
        {
            uint count = U32();
            if (count == uint.MaxValue) return 0;
            if (count > 100_000 || count * (long)minimumRecordSize > end - pos)
                throw new InvalidDataException("Unbounded stair layout count");
            return (int)count;
        }
        Pointer Ptr()
        {
            uint handle = U32();
            if (handle == 0) return new(handle, null);
            Need(2); ushort cls = BitConverter.ToUInt16(bytes, pos); pos += 2;
            if (cls != 0)
            {
                if (!RvtIO.Parsing.TfBlockDeserializer.Schemas.ContainsKey(cls))
                    throw new InvalidDataException("Unsupported stair layout pointer tag");
                pointers.Add((cls, handle));
            }
            return new(handle, cls);
        }
        CurveChain[] Chains()
        {
            var chains = new CurveChain[Count(8)];
            for (int i = 0; i < chains.Length; i++)
            {
                var records = new CurveData[Count(16)];
                for (int j = 0; j < records.Length; j++) records[j] = new(I32(), I32(), I32(), I32());
                var curves = new Pointer[Count(4)];
                for (int j = 0; j < curves.Length; j++) curves[j] = Ptr();
                chains[i] = new(records, curves);
            }
            return chains;
        }
    }

    internal static bool TryCompatibleCurveInfo(byte[] bytes, int start, int end,
        out int finish, out Entry[][] maps)
    {
        finish = -1; maps = [];
        if (start < 0 || end < 0 || end > bytes.Length || start > end - 4) return false;
        int pos = start;
        uint count = BitConverter.ToUInt32(bytes, pos); pos += 4;
        if (count == uint.MaxValue) count = 0;
        if (count > 100_000 || count * 4L > end - pos) return false;
        var result = new Entry[(int)count][];
        for (int i = 0; i < result.Length; i++)
        {
            if (pos > end - 4) return false;
            uint length = BitConverter.ToUInt32(bytes, pos); pos += 4;
            if (length == uint.MaxValue) length = 0;
            if (length > 100_000 || length * 16L > end - pos) return false;
            var entries = new Entry[(int)length];
            for (int j = 0; j < entries.Length; j++, pos += 16)
            {
                double parameter = BitConverter.ToDouble(bytes, pos);
                if (!double.IsFinite(parameter)) return false;
                entries[j] = new(parameter, BitConverter.ToInt32(bytes, pos + 8), BitConverter.ToInt32(bytes, pos + 12));
            }
            result[i] = entries;
        }
        finish = pos; maps = result;
        return true;
    }
}
