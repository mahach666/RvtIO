using RvtIO.Internal;

namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    /// <summary>The CEILING_THICKNESS getter of CompoundCeilingType/HostObjAttr, bounded to an own thick class-646 root
    /// and a null vertical-region structure. A non-null vertical-grid branch is deliberately not a layer sum.
    /// Later deferred objects are outside the complete own CompoundStructure prefix.</summary>
    internal static bool TryExtractCompoundCeilingTypeThicknessFeet(
        byte[] data, int start, uint size, ushort classId, out double thicknessFeet)
        => TryExtractHostTotalLayersWidth(data, start, size, classId, 646, out thicknessFeet);

    // Same getter as above; non-null vertical structures are not a layer sum.
    internal static bool TryExtractBasicWallTypeThicknessFeet(
        byte[] data, int start, uint size, ushort classId, out double thicknessFeet)
    {
        if (TryExtractHostTotalLayersWidth(data, start, size, classId, 512, out thicknessFeet)) return true;
        if (classId != 512 || !TryExtractHostTypeVerticalRegions(data, start, size, classId, out var grid))
            return false;
        // The envelope of a rectangular grid uses orientation == 0 for U, +/-1e30 sentinels
        // and ordered comparisons (not layer topology).
        double low = 1e30, high = -1e30;
        foreach (var segment in grid.Segments)
        {
            if (segment.Orientation != 0) continue;
            if (low > segment.Coordinate) low = segment.Coordinate;
            if (segment.Coordinate > high) high = segment.Coordinate;
        }
        thicknessFeet = high - low;
        return double.IsFinite(thicknessFeet);
    }

    private static bool TryExtractHostTotalLayersWidth(
        byte[] data, int start, uint size, ushort classId, ushort expectedClass, out double thicknessFeet)
    {
        thicknessFeet = 0;
        if (classId != expectedClass || !HasExactOwnElementRoot(data, start, size, classId)) return false;
        var fields = SerializationFields(classId);
        if (fields == null
            || classId == 646 && !HasExactSchemaField(fields, 646, "m_isThick", 1, 0, null)
            || !HasExactSchemaField(fields, 105, "m_pCompoundStructure", 14, 1, null))
            return false;

        var cursor = new Cursor
        {
            D = data, Pos = start + 14, End = start + 12 + checked((int)size),
            RootClassId = classId,
        };
        var queue = new List<(ushort Cls, uint Id)>();
        var root = new NativeParams();
        int compoundIndex = -1;
        bool? compoundIsNull = null;
        byte? isThick = null;
        try
        {
            foreach (var field in fields)
            {
                if (field.Declaring == 646 && field.Name == "m_isThick")
                {
                    if (!cursor.Take(1)) return false;
                    isThick = data[cursor.Pos - 1];
                }
                else if (field.Declaring == 105 && field.Name == "m_pCompoundStructure")
                {
                    int position = cursor.Pos, before = queue.Count;
                    ReadPtr(cursor, queue);
                    compoundIsNull = BitConverter.ToUInt32(data, position) == 0;
                    if (compoundIsNull == false)
                    {
                        if (queue.Count != before + 1 || queue[before].Cls != 737) return false;
                        compoundIndex = before;
                    }
                }
                else ReadField(cursor, field, queue, 0, root);
            }
            // Boolean serialization is bounded to 0/1; a non-canonical byte is not
            // promoted to the "any nonzero" in-memory bool.
            if (classId == 646 && isThick != 1 || compoundIsNull == null
                || !root.Fields.TryGetValue("m_id", out int owner) || owner <= 0
                || (uint)owner != BitConverter.ToUInt32(data, start)) return false;

            if (compoundIsNull == true)
            {
                for (int i = 0; i < queue.Count; i++) ReadDeferredObject(cursor, queue[i].Cls, queue);
                return cursor.Pos == cursor.End; // original null structure -> +0.0
            }

            for (int i = 0; i < compoundIndex; i++) ReadDeferredObject(cursor, queue[i].Cls, queue);
            if (!TryReadNamedCompoundStructure(cursor, queue, out var layers,
                    out _, out bool? verticalIsNull, allowNullLayerContainer: true)
                || verticalIsNull != true || layers == null
                || (compoundIndex + 1 == queue.Count && cursor.Pos != cursor.End)) return false;

            double total = 0;
            foreach (RawCompoundStructureLayer layer in layers)
            {
                double width = BitConverter.Int64BitsToDouble(layer.LayerWidthBits);
                if (!double.IsFinite(width)) return false;
                total += width; // source ADDSD, index 0..count-1; no decimal/rounding
                if (!double.IsFinite(total)) return false;
            }
            thicknessFeet = total;
            return true;
        }
        catch (DecodeFail) { return false; }
        catch (OverflowException) { return false; }
    }
}
