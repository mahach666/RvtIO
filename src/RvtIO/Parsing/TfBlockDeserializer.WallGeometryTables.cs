namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record WallGeomTableEntry(int Index, int GeneratorId, int Start, int End);
    internal sealed record WallOwnGeometryTable(int QueueIndex, uint Handle, int Start, int End,
        int MaxSafeTag, int LastCheckedKingsUserModificationDate, byte RefPointMirrored,
        uint EntryCount, int EntriesStart, int EntriesEnd, IReadOnlyList<WallGeomTableEntry> Entries,
        WallPhasePointer BigTableOwner, WallPhaseArray MaterialMarkers, WallPhaseArray FaceTypeMarkers);

    private static bool HasOwnWallGeometryTableSchema()
    {
        var fields = SerializationFields(2087);
        var entry = SerializationFields(533);
        return fields is { Count: 7 }
            && fields[0] is { Declaring: 2087, Name: "m_maxSafeTag", Type: 4, Mods: 0, Ref: null, Cnt: null }
            && fields[1] is { Declaring: 2087, Name: "m_lastCheckedKingsUserModificationDate", Type: 4, Mods: 0, Ref: null, Cnt: null }
            && fields[2] is { Declaring: 2087, Name: "m_refPntMirrored", Type: 1, Mods: 0, Ref: null, Cnt: null }
            && fields[3] is { Declaring: 2087, Name: "m_table", Type: 14, Mods: 80, Ref: 533, Cnt: null }
            && fields[4] is { Declaring: 2087, Name: "m_bigTableOwner", Type: 14, Mods: 1, Ref: null, Cnt: null }
            && fields[5] is { Declaring: 2087, Name: "m_materialMarkers", Type: 14, Mods: 81, Ref: null, Cnt: null }
            && fields[6] is { Declaring: 2087, Name: "m_faceTypeMarkers", Type: 14, Mods: 81, Ref: null, Cnt: null }
            && entry is { Count: 1 }
            && entry[0] is { Declaring: 533, Name: "m_geomGeneratorId", Type: 4, Mods: 0, Ref: 533, Cnt: null };
    }

    // GeomTabEntry has a transient m_pGNode and a persisted generator id.
    // Index is the persisted array slot, not an active Face id or a resolved node.
    // Big-table/marker payloads and pointer reconstruction are not decoded here.
    private static WallOwnGeometryTable ReadOwnWallGeometryTable(Cursor c, List<(ushort, uint)> queue,
        int frameStart, int queueIndex, uint handle)
    {
        int tableStart = c.Pos;
        int maxSafeTag = ReadI32(c), date = ReadI32(c);
        if (!c.Take(1)) throw Fail;
        byte mirrored = c.D[c.Pos - 1];
        int entriesStart = c.Pos;
        uint count = ReadU32(c);
        var entries = new List<WallGeomTableEntry>();
        if (count != uint.MaxValue)
        {
            if (count > 100_000 || count > (c.End - c.Pos) / 4) throw Fail;
            for (int index = 0; index < count; index++)
            {
                int entryStart = c.Pos;
                int id = ReadI32(c); // scalar Ref:533 metadata does not make this recursive
                entries.Add(new(index, id, entryStart - frameStart, c.Pos - frameStart));
            }
        }
        int entriesEnd = c.Pos;
        var owner = Pointer();
        var material = Markers("m_materialMarkers");
        var faceType = Markers("m_faceTypeMarkers");
        return new(queueIndex, handle, tableStart - frameStart, c.Pos - frameStart,
            maxSafeTag, date, mirrored, count, entriesStart - frameStart, entriesEnd - frameStart,
            entries, owner, material, faceType);

        WallPhasePointer Pointer()
        {
            int pointerStart = c.Pos, before = queue.Count;
            ReadPtr(c, queue);
            if (queue.Count > 100_000) throw Fail;
            uint value = BitConverter.ToUInt32(c.D, pointerStart);
            ushort? cls = value == 0 ? null : BitConverter.ToUInt16(c.D, pointerStart + 4);
            return new(value, cls, queue.Count == before ? null : before,
                pointerStart - frameStart, c.Pos - frameStart);
        }

        WallPhaseArray Markers(string name)
        {
            int arrayStart = c.Pos;
            uint length = ReadU32(c);
            var members = new List<WallPhasePointer>();
            if (length != uint.MaxValue)
            {
                if (length > 100_000 || length > (c.End - c.Pos) / 4) throw Fail;
                for (uint index = 0; index < length; index++) members.Add(Pointer());
            }
            return new(name, length, arrayStart - frameStart, c.Pos - frameStart, members);
        }
    }
}
