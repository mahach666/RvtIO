namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    // The geometry rule descriptor callbacks bind int -> FaceGeometry/EdgeGeometry.
    // Correct these four absent inline references only, not the shared descriptors.
    private static bool TryReadOriginalWallBackupField(Cursor c, TfField f,
        List<(ushort, uint)> queue, int depth, NativeParams? capture)
    {
        if (f.Type != 14 || f.Ref != null || f.Cnt != null) return false;
        int target = (f.Declaring, f.Name, f.Mods) switch
        {
            (1591, "m_faceBackup", 80) => 1592,
            (1249, "m_edgeBackup", 80) => 1250,
            (1592, "second", 0) => 1593,
            (1250, "second", 0) => 1251,
            _ => 0
        };
        if (target == 0) return false;
        if (depth > 40 || !HasOriginalWallBackupSchemas()) throw Fail;
        ReadField(c, f with { Ref = target }, queue, depth, capture);
        return true;
    }

    private static bool HasOriginalWallBackupSchemas()
    {
        foreach (var (id, name) in new (int, string)[]
        {
            (1248, "EdgePnt"), (1249, "EdgeBackupMgr"),
            (1250, "std::pair< int, EdgeGeometry >"), (1251, "EdgeGeometry"),
            (1252, "NodeGeometry"), (1591, "FaceBackupMgr"),
            (1592, "std::pair< int, FaceGeometry >"), (1593, "FaceGeometry")
        })
            if (!Schemas.TryGetValue(id, out var schema) || schema.Name != name) return false;
        if (Schemas[1251].Base != 1252 || Schemas[1593].Base != 1252
            || Schemas[1252].Base != null || Schemas[1252].Fields is not { Count: 0 }) return false;
        return Match(1248, ("uv", 13, 16, 1248, 2))
            && Match(1249, ("m_edgeBackup", 14, 80, null, null))
            && Match(1250, ("first", 4, 0, null, null), ("second", 14, 0, null, null))
            && Match(1251, ("m_interiorEdgePnts", 14, 80, 1248, null),
                ("m_firstAndLastEdgePnts", 14, 16, 1248, 2), ("m_flags", 2, 0, 1250, null))
            && Match(1591, ("m_faceBackup", 14, 80, null, null))
            && Match(1592, ("first", 4, 0, null, null), ("second", 14, 0, null, null))
            && Match(1593, ("m_flipOrient", 1, 0, null, null), ("m_pSurf", 14, 1, 1592, null));

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
