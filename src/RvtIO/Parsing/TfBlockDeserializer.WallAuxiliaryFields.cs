namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    // PairSerialization descriptor contracts. These correct missing concrete references
    // only; the shared r22 descriptors are not modified.
    private static bool TryReadOriginalWallAuxiliaryField(Cursor c, TfField f,
        List<(ushort, uint)> queue, int depth, NativeParams? capture)
    {
        if (f.Type != 14 || f.Cnt != null) return false;
        int target = (f.Declaring, f.Name, f.Mods, f.Ref) switch
        {
            (20, "m_id", 0, null) => 21,
            (532, "m_bigTable", 80, null) => 533,
            (877, "m_stableFaceRefsMap", 80, null) => 878,
            (878, "second", 0, null) => 879,
            (879, "m_setting", 0, 878) => 20,
            _ => 0
        };
        if (target != 0)
        {
            if (depth > 40 || !HasOriginalWallAuxiliarySchemas()) throw Fail;
            ReadField(c, f with { Ref = target }, queue, depth, capture);
            return true;
        }
        if (f.Declaring != 496 || f.Mods != 80 || f.Ref != null
            || f.Name is not ("m_PatternPositionMap" or "m_substituteFaceMap")) return false;
        if (depth > 40 || !HasOriginalWallAuxiliarySchemas()) throw Fail;
        uint count = ReadU32(c);
        if (count == uint.MaxValue) return true;
        int valueClass = f.Name == "m_PatternPositionMap" ? 78 : 91;
        int width = valueClass == 78 ? 100 : 12; // int + 12 f64; int + two inline identifiers
        if (count > 1_000_000 || count > (uint)((c.End - c.Pos) / width)) throw Fail;
        for (uint i = 0; i < count; i++)
        {
            ReadI32(c); // first callback's original primitive type4
            ReadInlineStruct(c, valueClass, queue, depth + 1);
        }
        return true;
    }

    private static bool HasOriginalWallAuxiliarySchemas()
    {
        foreach (var (id, name) in new (int, string)[]
        {
            (20, "ElementId"), (21, "Identifier"), (78, "Trf"), (91, "ElementIdPlusIndex"),
            (140, "GeomRef"), (496, "PatternHelper"), (532, "BigArrGeomTabEntryWrapper"),
            (533, "GeomTabEntry"), (875, "CoverFaceStableRef"), (877, "CoverSettingsCell"),
            (878, "std::pair< CoverFaceStableRef, HostFaceInfo >"), (879, "HostFaceInfo")
        })
            if (!Schemas.TryGetValue(id, out var schema) || schema.Name != name) return false;
        return Match(20, ("m_id", 14, 0, null, null))
            && Match(21, ("m_id", 4, 0, 20, null))
            && Match(78, ("m_3x3", 13, 16, null, 3), ("m_or", 7, 16, 77, 3))
            && Match(91, ("m_elementId", 14, 0, 20, null), ("m_index", 4, 0, 90, null))
            && Match(533, ("m_geomGeneratorId", 4, 0, 533, null))
            && Match(875, ("m_dist", 7, 0, null, null), ("m_faceType", 4, 0, null, null),
                ("m_geomRef", 14, 0, 140, null))
            && Match(878, ("first", 14, 0, 875, null), ("second", 14, 0, null, null))
            && Match(879, ("m_paramId", 14, 0, 20, null), ("m_setting", 14, 0, 878, null));

        static bool Match(int cls, params (string Name, int Type, int Mods, int? Ref, int? Count)[] wanted)
        {
            var fields = SerializationFields(cls);
            if (fields == null || fields.Count != wanted.Length) return false;
            for (int i = 0; i < wanted.Length; i++)
            {
                var actual = fields[i]; var expected = wanted[i];
                if (actual.Declaring != cls || actual.Name != expected.Name || actual.Type != expected.Type
                    || actual.Mods != expected.Mods || actual.Ref != expected.Ref || actual.Cnt != expected.Count)
                    return false;
            }
            return true;
        }
    }
}
