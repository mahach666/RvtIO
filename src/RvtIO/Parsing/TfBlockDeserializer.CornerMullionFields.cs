namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    // Private HostObjDB field descriptors and the typed ElementId getter.
    private static bool TryReadOriginalCornerMullionField(Cursor c, TfField f,
        List<(ushort, uint)> queue, int depth, NativeParams? capture)
    {
        if (f.Type != 14 || f.Mods != 80 || f.Cnt != null) return false;
        int target = (f.Declaring, f.Name, f.Ref) switch
        {
            (588, "m_edgesToNbrCornerMullions", null) => 589,
            (589, "m_mullionIds", 589) => 20,
            _ => 0
        };
        if (target == 0) return false;
        if (depth > 40 || !HasOriginalCornerMullionSchemas()) throw Fail;
        ReadField(c, f with { Ref = target }, queue, depth, capture);
        return true;
    }

    private static bool HasOriginalCornerMullionSchemas()
    {
        if (!Schemas.TryGetValue(588, out var owner) || owner.Name != "CGDriverNbrCornerMullionData"
            || owner.Base != null || !Schemas.TryGetValue(589, out var value)
            || value.Name != "EdgeToMullionInfo" || value.Base != null
            || !Schemas.TryGetValue(587, out var mullion) || mullion.Name != "MullionInfo"
            || !Schemas.TryGetValue(20, out var id) || id.Name != "ElementId") return false;
        var ownerFields = SerializationFields(588);
        var valueFields = SerializationFields(589);
        var mullionFields = SerializationFields(587);
        return ownerFields is { Count: 2 }
            && ownerFields[0] is { Declaring: 588, Name: "m_nbrCornerMullions", Type: 14, Mods: 80, Ref: 587, Cnt: null }
            && ownerFields[1] is { Declaring: 588, Name: "m_edgesToNbrCornerMullions", Type: 14, Mods: 80, Ref: null, Cnt: null }
            && valueFields is { Count: 2 }
            && valueFields[0] is { Declaring: 589, Name: "m_edgeTag", Type: 4, Mods: 0, Ref: null, Cnt: null }
            && valueFields[1] is { Declaring: 589, Name: "m_mullionIds", Type: 14, Mods: 80, Ref: 589, Cnt: null }
            && mullionFields is { Count: 2 }
            && mullionFields[0] is { Declaring: 587, Name: "m_mullionId", Type: 14, Mods: 0, Ref: 20, Cnt: null }
            && mullionFields[1] is { Declaring: 587, Name: "m_edgeTag", Type: 4, Mods: 0, Ref: 587, Cnt: null };
    }
}
