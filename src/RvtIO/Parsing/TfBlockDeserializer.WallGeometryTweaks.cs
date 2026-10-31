namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record OwnWallSixPhaseGeometryHistories(OwnWallGeometryHistories Histories,
        IReadOnlyList<WallTweakOwnTail> TweakOwnTails, IReadOnlyList<WallGridOwnTail> GridOwnTails,
        IReadOnlyList<WallCutoutSketchOwnTail> CutoutSketchOwnTails);
    internal sealed record WallRawIntList(uint Count, int Start, int End, IReadOnlyList<int> Values);
    internal sealed record WallAffectedTagEntry(int ElementId, WallRawIntList Tags, int Start, int End);
    internal sealed record WallAffectedTagMap(uint Count, int Start, int End,
        IReadOnlyList<WallAffectedTagEntry> Entries);
    internal sealed record WallPairOwnIdentity(int OtherId, byte UserSet, byte AllowUserUnjoin,
        byte IsAssociatedWithColJoinStep);
    internal sealed record WallCutoutSketchOwnTail(int QueueIndex, int Start, int End,
        int SketchId, int UserId, byte IsTrueBoolean, byte IsProjectOpening);
    internal sealed record WallTweakOwnTail(int QueueIndex, ushort ClassId, int Start, int End,
        WallPairOwnIdentity? Pair, WallAffectedTagMap AffectedEdgesTags,
        WallRawIntList? IdGeomJoined, WallRawIntList HealingParents, byte? HasCoincFaces, byte? DoNothing);

    /// <summary>All six own ordered phase lists, through the fields of the complete last step. Later snapshots, big
    /// tables, geometry nodes and pointer reconstruction are not decoded. Complete means this requested FIFO prefix,
    /// not physical geometry.</summary>
    internal static OwnWallSixPhaseGeometryHistories? ExtractOwnWallSixPhaseGeometryHistories(
        byte[] data, int start, uint size, ushort classId, out string? failure)
    {
        var histories = ExtractOwnWallGeometryHistoriesCore(data, start, size, classId, true,
            out failure, out var tweaks, out var grids, out var cutouts, out _, out _);
        return histories == null ? null : new(histories, tweaks, grids, cutouts);
    }

    private static bool HasWallCutoutSketchOwnTailSchema()
    {
        var fields = SerializationFields(982);
        var inherited = SerializationFields(96);
        return fields is { Count: 12 } && inherited is { Count: 8 }
            && fields.Take(8).SequenceEqual(inherited)
            && fields[8] is { Declaring: 982, Name: "m_sketchId", Type: 14, Mods: 0, Ref: 20, Cnt: null }
            && fields[9] is { Declaring: 982, Name: "m_userId", Type: 14, Mods: 0, Ref: 20, Cnt: null }
            && fields[10] is { Declaring: 982, Name: "m_isTrueBoolean", Type: 1, Mods: 0, Ref: null, Cnt: null }
            && fields[11] is { Declaring: 982, Name: "m_isProjectOpening", Type: 1, Mods: 0, Ref: null, Cnt: null };
    }

    private static WallCutoutSketchOwnTail ReadWallCutoutSketchOwnTail(Cursor c, int start, int index)
    {
        int first = c.Pos;
        int sketch = ReadI32(c), user = ReadI32(c);
        byte boolean = ReadWallOwnByte(c), opening = ReadWallOwnByte(c);
        return new(index, first - start, c.Pos - start, sketch, user, boolean, opening);
    }

    private static bool HasWallTweakOwnTailSchema(ushort cls)
    {
        var inherited = SerializationFields(96);
        var fields = SerializationFields(cls);
        if (inherited is not { Count: 8 } || fields == null
            || fields.Count != (cls == 4147 ? 11 : cls == 677 ? 16 : 0)
            || !fields.Take(8).SequenceEqual(inherited)) return false;
        int map = cls == 4147 ? 8 : 12;
        if (fields[map].Declaring != cls || fields[map] is not
            { Name: "m_affectedEdgesTags", Type: 14, Mods: 80, Cnt: null }
            || fields[map].Ref != (cls == 4147 ? 679 : null)) return false;
        var pair = SerializationFields(679);
        if (pair is not { Count: 2 }
            || pair[0] is not { Declaring: 679, Name: "first", Type: 14, Mods: 0, Ref: 20, Cnt: null }
            || pair[1] is not { Declaring: 679, Name: "second", Type: 14, Mods: 0, Ref: null, Cnt: null })
            return false;
        if (cls == 4147)
            return fields[9] is { Declaring: 4147, Name: "m_idGeomJoined", Type: 14, Mods: 80, Ref: 20, Cnt: null }
                && fields[10] is { Declaring: 4147, Name: "m_healingParents", Type: 4, Mods: 80, Ref: null, Cnt: null };
        return fields[8] is { Declaring: 678, Name: "m_otherId", Type: 14, Mods: 0, Ref: 20, Cnt: null }
            && fields[9] is { Declaring: 678, Name: "m_userSet", Type: 1, Mods: 0, Ref: null, Cnt: null }
            && fields[10] is { Declaring: 678, Name: "m_allowUserUnjoin", Type: 1, Mods: 0, Ref: null, Cnt: null }
            && fields[11] is { Declaring: 678, Name: "m_isAssociatedWithColJoinStep", Type: 1, Mods: 0, Ref: null, Cnt: null }
            && fields[13] is { Declaring: 677, Name: "m_healingParents", Type: 4, Mods: 80, Ref: null, Cnt: null }
            && fields[14] is { Declaring: 677, Name: "m_hasCoincFaces", Type: 1, Mods: 0, Ref: null, Cnt: null }
            && fields[15] is { Declaring: 677, Name: "m_doNothing", Type: 1, Mods: 0, Ref: null, Cnt: null };
    }

    private static uint ReadWallOwnCount(Cursor c, int minimumRowBytes)
    {
        uint count = ReadU32(c);
        if (count != uint.MaxValue && (count > 100_000 || count > (c.End - c.Pos) / minimumRowBytes)) throw Fail;
        return count;
    }

    private static byte ReadWallOwnByte(Cursor c)
    {
        if (!c.Take(1)) throw Fail;
        return c.D[c.Pos - 1];
    }

    private static WallRawIntList ReadWallRawIntList(Cursor c, int start)
    {
        int first = c.Pos;
        uint count = ReadWallOwnCount(c, 4);
        var values = new List<int>();
        if (count != uint.MaxValue)
            for (uint i = 0; i < count; i++) values.Add(ReadI32(c));
        return new(count, first - start, c.Pos - start, values);
    }

    private static WallTweakOwnTail ReadWallTweakOwnTail(Cursor c, ushort cls, int start, int index)
    {
        int first = c.Pos;
        WallPairOwnIdentity? pair = cls == 677
            ? new(ReadI32(c), ReadWallOwnByte(c), ReadWallOwnByte(c), ReadWallOwnByte(c)) : null;
        int mapStart = c.Pos;
        uint count = ReadWallOwnCount(c, 8);
        var entries = new List<WallAffectedTagEntry>();
        if (count != uint.MaxValue)
            for (uint i = 0; i < count; i++)
            {
                int rowStart = c.Pos;
                int id = ReadI32(c);
                // The second PairSerialization delegate is an inline complex (000E),
                // whose TweakEffectedTagSetWrapper.m_tags is a counted signed int (5004).
                // There is no polymorphic handle and no deferred pointer here.
                var tags = ReadWallRawIntList(c, start);
                entries.Add(new(id, tags, rowStart - start, c.Pos - start));
            }
        var map = new WallAffectedTagMap(count, mapStart - start, c.Pos - start, entries);
        var joined = cls == 4147 ? ReadWallRawIntList(c, start) : null;
        var healing = ReadWallRawIntList(c, start);
        byte? coincident = cls == 677 ? ReadWallOwnByte(c) : null;
        byte? nothing = cls == 677 ? ReadWallOwnByte(c) : null;
        return new(index, cls, first - start, c.Pos - start, pair, map, joined, healing, coincident, nothing);
    }
}
