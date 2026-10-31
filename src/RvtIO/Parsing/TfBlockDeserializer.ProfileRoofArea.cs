namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record ProfileRoofAreaFaceTags(int OwnerId, int[] Top, int[] Bottom);

    // A profile roof selects keys[0] of the first ProfileRoofBaseGStep.
    internal static ProfileRoofAreaFaceTags? ExtractOwnProfileRoofAreaFaceTags(byte[] data, int start, uint size)
    {
        if (!HasExactOwnElementRoot(data, start, size, 3054) || !HasCountedWallHistorySchema()) return null;
        var cursor = new Cursor { D = data, Pos = start + 14, End = start + 12 + (int)size,
            RootClassId = 3054, ExactFamilyCutInputs = true };
        var queue = new List<(ushort Cls, uint Id)>();
        int listIndex = -1, baseIndex = -1;
        try
        {
            foreach (var field in SerializationFields(3054)!)
            {
                int before = queue.Count;
                ReadField(cursor, field, queue, 0, null);
                if (field is { Declaring: 37, Name: "m_geomSteps", Type: 14, Mods: 1 }
                    && queue.Count == before + 1 && queue[before].Cls == 2086) listIndex = before;
            }
            if (listIndex < 0) return null;
            for (int index = 0; index < queue.Count && index < 100_000; index++)
            {
                ushort cls = queue[index].Cls;
                var fields = SerializationFields(cls);
                if (fields == null) return null;
                if (index == baseIndex)
                {
                    var top = new List<int>(); var bottom = new List<int>();
                    var seen = new HashSet<int>();
                    foreach (var field in fields)
                    {
                        int position = cursor.Pos;
                        ReadField(cursor, field, queue, 1, null);
                        if (field is { Declaring: 97, Name: "m_faceHistTable", Type: 14, Mods: 80 })
                        {
                            uint count = BitConverter.ToUInt32(data, position);
                            if (count > 100_000 || position + 4L + count * 16 != cursor.Pos) return null;
                            for (int row = 0; row < count; row++)
                            {
                                int offset = position + 4 + row * 16;
                                int tag = BitConverter.ToInt32(data, offset), key = BitConverter.ToInt32(data, offset + 4);
                                if (!seen.Add(tag)) return null;
                                if (key == 24) top.Add(tag);
                                if (key == 10) bottom.Add(tag);
                            }
                        }
                        if (field is { Declaring: 96, Name: "m_version" }
                            && BitConverter.ToInt32(data, position) != 33) return null;
                        if (field is { Declaring: 96, Name: "m_pElem", Type: 14, Mods: 3 }
                            && BitConverter.ToUInt32(data, position) != 2) return null;
                    }
                    return top.Count > 0 && bottom.Count > 0
                        ? new(BitConverter.ToInt32(data, start), top.ToArray(), bottom.ToArray()) : null;
                }
                foreach (var field in fields)
                {
                    int position = cursor.Pos, before = queue.Count;
                    ReadField(cursor, field, queue, 1, null);
                    if (index != listIndex) continue;
                    if (field is { Declaring: 2086, Name: "m_bRepFormGList", Type: 14, Mods: 81 })
                    {
                        uint count = BitConverter.ToUInt32(data, position);
                        if (count > 100_000 || queue.Count - before != count) return null;
                        var bases = Enumerable.Range(before, (int)count).Where(q => queue[q].Cls == 3057).ToArray();
                        if (bases.Length != 1) return null;
                        baseIndex = bases[0];
                    }
                    if (field is { Declaring: 2086, Name: "m_pElem", Type: 14, Mods: 3 }
                        && BitConverter.ToUInt32(data, position) != 2) return null;
                }
            }
        }
        catch (DecodeFail) { return null; }
        return null;
    }
}
