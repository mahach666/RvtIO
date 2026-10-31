namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record WallSimpleOwnTail(int QueueIndex, ushort ClassId, int Start, int End,
        int? GeneratedWallCrossSection, WallPhasePointer? Geometry,
        IReadOnlyList<int>? SideTags, IReadOnlyList<byte>? Sides);

    private static bool HasSimpleWallOwnTailSchema(ushort cls)
    {
        var fields = SerializationFields(cls);
        var inherited = SerializationFields(96);
        int count = cls == 500 ? 9 : 11;
        if (fields == null || inherited is not { Count: 8 } || fields.Count != count
            || !fields.Take(8).SequenceEqual(inherited)) return false;
        return cls == 500
            ? fields[8] is { Declaring: 500, Name: "m_generatedWallCrossSection", Type: 4,
                Mods: 0, Ref: null, Cnt: null }
            : cls == 4165
                && fields[8] is { Declaring: 4165, Name: "m_oGeom", Type: 14, Mods: 1, Ref: null, Cnt: null }
                && fields[9] is { Declaring: 4165, Name: "m_sideTags", Type: 4, Mods: 16, Ref: null, Cnt: 2 }
                && fields[10] is { Declaring: 4165, Name: "m_sides", Type: 1, Mods: 16, Ref: null, Cnt: 2 };
    }

    // The named descriptors and field managers define these own layouts independently of the
    // persisted GeomStep.m_version scalar. The fields and the raw geometry pointer are kept;
    // this does not resolve geometry, select active faces or evaluate extrusion.
    private static WallSimpleOwnTail ReadSimpleWallOwnTail(Cursor c, ushort cls,
        List<(ushort, uint)> queue, int frameStart, int queueIndex)
    {
        int tailStart = c.Pos;
        if (cls == 500)
        {
            int crossSection = ReadI32(c);
            return new(queueIndex, cls, tailStart - frameStart, c.Pos - frameStart,
                crossSection, null, null, null);
        }

        int pointerStart = c.Pos, before = queue.Count;
        ReadPtr(c, queue);
        if (queue.Count > 100_000) throw Fail;
        uint handle = BitConverter.ToUInt32(c.D, pointerStart);
        ushort? geometryClass = handle == 0 ? null : BitConverter.ToUInt16(c.D, pointerStart + 4);
        var geometry = new WallPhasePointer(handle, geometryClass,
            queue.Count == before ? null : before, pointerStart - frameStart, c.Pos - frameStart);
        int[] tags = [ReadI32(c), ReadI32(c)];
        if (!c.Take(2)) throw Fail;
        var sides = new List<byte> { c.D[c.Pos - 2], c.D[c.Pos - 1] };
        return new(queueIndex, cls, tailStart - frameStart, c.Pos - frameStart,
            null, geometry, tags, sides);
    }
}
