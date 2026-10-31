namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    // CGDriver/Mullion field descriptors and the ElementIdIndexPairSet descriptor.
    // Supply only absent concrete value references; the existing grammar is kept.
    private static bool TryReadOriginalCurtainAuxiliaryField(Cursor c, TfField f,
        List<(ushort, uint)> queue, int depth, NativeParams? capture)
    {
        if (f.Type != 14 || f.Ref != null) return false;
        int target = (f.Declaring, f.Name, f.Mods, f.Cnt) switch
        {
            (368, "m_ruleInst", 16, 2) => 369,
            (368, "m_mullionJoins", 80, null) => 370,
            (370, "m_gridJoinSet", 80, null) => 371,
            (586, "m_mullionsById", 80, null) => 587,
            (90, "m_data", 80, null) => 91,
            _ => 0
        };
        if (target == 0) return false;
        if (depth > 40 || !HasOriginalCurtainAuxiliarySchemas()) throw Fail;
        ReadField(c, f with { Ref = target }, queue, depth, capture);
        return true;
    }

    private static bool HasOriginalCurtainAuxiliarySchemas()
    {
        foreach (var (id, name) in new (int, string)[]
        {
            (90, "ElementIdIndexPairSet"), (91, "ElementIdPlusIndex"),
            (368, "CGDriver"), (369, "SpacingRuleInst"), (370, "GridJoinInfo"),
            (371, "GridSegAndEnd"), (586, "CGDriverMullionData"), (587, "MullionInfo")
        })
            if (!Schemas.TryGetValue(id, out var schema) || schema.Name != name || schema.Base != null)
                return false;
        return Match(90, ("m_data", 14, 80, null, null))
            && Match(91, ("m_elementId", 14, 0, 20, null), ("m_index", 4, 0, 90, null))
            && Match(368, ("m_pCurtainGrid", 14, 1, null, null), ("m_mullionData", 14, 1, null, null),
                ("m_nbrCornerMullionData", 14, 1, null, null), ("m_ruleInst", 14, 16, null, 2),
                ("m_mullionJoins", 14, 80, null, null), ("m_oOrigGridSurf", 14, 1, null, null),
                ("m_oCGDriverCSys", 14, 1, null, null), ("m_mullionJoinId", 4, 0, null, null))
            && Match(369, ("m_angle", 7, 0, null, null), ("m_origin", 7, 0, null, null),
                ("m_belt", 7, 0, null, null), ("m_margins", 7, 16, null, 2),
                ("m_fixedNum", 4, 0, null, null), ("m_justify", 4, 0, null, null), ("m_beltSet", 1, 0, 369, null))
            && Match(370, ("m_gridJoinSet", 14, 80, null, null), ("m_id", 4, 0, null, null),
                ("m_constULinesButt", 1, 0, 370, null))
            && Match(371, ("m_edgeTag", 4, 0, null, null), ("m_end", 4, 0, 371, null))
            && Match(586, ("m_mullionsById", 14, 80, null, null), ("m_mullionsByEdge", 14, 80, 587, null))
            && Match(587, ("m_mullionId", 14, 0, 20, null), ("m_edgeTag", 4, 0, 587, null));

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
