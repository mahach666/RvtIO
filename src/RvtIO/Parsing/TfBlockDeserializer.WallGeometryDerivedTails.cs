namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record WallIntElementPair(int First, int ElementId, int Start, int End);
    internal sealed record WallLayerExtensionSide(string Name, uint Count, int Start, int End,
        IReadOnlyList<WallIntElementPair> Entries);
    internal sealed record WallLayerExtensionOwnTail(int QueueIndex, ushort ClassId, int Start, int End,
        IReadOnlyList<WallLayerExtensionSide> Sides);
    internal sealed record WallSweepIdentityOwnTail(int QueueIndex, ushort ClassId, int Start, int End,
        int WallSweepId, int? Side, int? FixedSweepInstanceTag);
    internal sealed record WallMemberGeometryOwnTail(int QueueIndex, ushort ClassId, int Start, int End,
        int MemberId, byte DidCutSomething);

    private static bool HasDerivedWallOwnTailSchema(ushort cls)
    {
        int count = cls switch { 150 or 4098 => 10, 4166 => 9, 4167 => 11, _ => 0 };
        var fields = SerializationFields(cls);
        var inherited = SerializationFields(96);
        if (count == 0 || fields == null || inherited is not { Count: 8 } || fields.Count != count
            || !fields.Take(8).SequenceEqual(inherited)) return false;
        if (cls == 150)
            return fields[8] is { Declaring: 150, Name: "m_memberId", Type: 14, Mods: 0, Ref: 20, Cnt: null }
                && fields[9] is { Declaring: 150, Name: "m_didCutSomething", Type: 1, Mods: 0, Ref: null, Cnt: null };
        if (cls == 4098)
        {
            var pair = SerializationFields(322);
            // PairSerialization delegates declare scalar int + ElementId.
            // Formats reports a self-ref (322) for the second; it is not a nested pair.
            return fields[8] is { Declaring: 4098, Name: "m_otherId0", Type: 14, Mods: 80, Ref: 322, Cnt: null }
                && fields[9] is { Declaring: 4098, Name: "m_otherId1", Type: 14, Mods: 80, Ref: 322, Cnt: null }
                && pair is { Count: 2 }
                && pair[0] is { Declaring: 322, Name: "first", Type: 4, Mods: 0, Ref: null, Cnt: null }
                && pair[1] is { Declaring: 322, Name: "second", Type: 14, Mods: 0, Ref: 322, Cnt: null };
        }
        return fields[8].Declaring == cls
            && fields[8] is { Name: "m_wallSweepId", Type: 14, Mods: 0, Ref: 20, Cnt: null }
            && (cls == 4166
                || fields[9] is { Declaring: 4167, Name: "m_side", Type: 4, Mods: 0, Ref: null, Cnt: null }
                && fields[10] is { Declaring: 4167, Name: "m_fixedSweepInstanceTag", Type: 4, Mods: 0, Ref: null, Cnt: null });
    }

    private static WallLayerExtensionOwnTail ReadLayerExtensionOwnTail(Cursor c, int frameStart, int queueIndex)
    {
        int tailStart = c.Pos;
        var sides = new List<WallLayerExtensionSide>();
        foreach (string name in new[] { "m_otherId0", "m_otherId1" })
        {
            int sideStart = c.Pos;
            uint count = ReadU32(c);
            var entries = new List<WallIntElementPair>();
            if (count != uint.MaxValue)
            {
                if (count > 100_000 || count > (c.End - c.Pos) / 8) throw Fail;
                for (uint index = 0; index < count; index++)
                {
                    int pairStart = c.Pos;
                    int key = ReadI32(c), elementId = ReadI32(c);
                    entries.Add(new(key, elementId, pairStart - frameStart, c.Pos - frameStart));
                }
            }
            sides.Add(new(name, count, sideStart - frameStart, c.Pos - frameStart, entries));
        }
        return new(queueIndex, 4098, tailStart - frameStart, c.Pos - frameStart, sides);
    }

    private static WallSweepIdentityOwnTail ReadSweepIdentityOwnTail(Cursor c, ushort cls,
        int frameStart, int queueIndex)
    {
        int tailStart = c.Pos;
        int sweepId = ReadI32(c); // original inline ElementId; no pointer or deferred queue
        int? side = cls == 4167 ? ReadI32(c) : null;
        int? tag = cls == 4167 ? ReadI32(c) : null;
        return new(queueIndex, cls, tailStart - frameStart, c.Pos - frameStart, sweepId, side, tag);
    }

    private static WallMemberGeometryOwnTail ReadMemberGeometryOwnTail(Cursor c, int frameStart, int queueIndex)
    {
        int tailStart = c.Pos;
        int memberId = ReadI32(c);
        if (!c.Take(1)) throw Fail;
        // The bool-sized byte is kept; it does not by itself evaluate the cut helper.
        return new(queueIndex, 150, tailStart - frameStart, c.Pos - frameStart, memberId, c.D[c.Pos - 1]);
    }
}
