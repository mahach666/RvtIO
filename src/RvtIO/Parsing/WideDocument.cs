namespace RvtIO.Parsing;

/// <summary>
/// The document level facts of a file with 64-bit ids that the number formatting needs, read with the schema-driven walk
/// (<see cref="WideBlockWalker"/>) instead of the Revit 2022 byte layouts: Global/Latest is an ADocument whose queued
/// objects are walked like an element block (the last 4 bytes are not read).
/// It names the units element (UnitsTracking.m_unitsElemId) and holds the two persisted schema maps of the unique
/// ESSchemaStorage (m_storedForgeSchemas: the unit schemas, m_storedParameterSchemas), both lists of text pairs. The
/// UnitsElem block of that id carries the FormatOptions per spec (AUnits.m_formatOptionsMap).
/// </summary>
internal static class WideDocument
{
    internal sealed record Result(uint UnitsElemId,
        TfBlockDeserializer.DocumentSchemaMap Units, TfBlockDeserializer.DocumentSchemaMap Parameters);

    /// <summary>The units element id and the persisted schema maps of a decoded Global/Latest stream; null when the stream is
    /// not an ADocument, the walk fails, or the storage / tracking objects are not unique.</summary>
    internal static Result? Read(byte[] latest, SchemaDocument schema)
    {
        if (latest.Length < 8) return null;
        var classes = schema.Classes.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        int root = BitConverter.ToUInt16(latest, 0);
        if (!classes.TryGetValue(root, out var document) || document.Name != "ADocument") return null;
        var walker = new WideBlockWalker(schema,
            (declaring, field) => declaring.Name switch
            {
                "ESSchemaStorage" when field.Name is "m_storedForgeSchemas" or "m_storedParameterSchemas" => field.Name,
                "UnitsTracking" when field.Name == "m_unitsElemId" => field.Name,
                _ => null,
            },
            ["std::pair< AString, AString >"]);
        var walk = walker.Run(latest, 0, latest.Length, root);
        if (walk.Error != null || !walk.RootRead) return null;

        int storage = -1, tracking = -1;
        for (int i = 0; i < walk.Queue.Count; i++)
        {
            string name = walker.Class(walk.Queue[i])!.Name;
            if (name == "ESSchemaStorage") { if (storage >= 0) return null; storage = i; }
            else if (name == "UnitsTracking") { if (tracking >= 0) return null; tracking = i; }
        }
        if (storage < 0 || tracking < 0) return null;
        var ids = walk.Captures.Where(c => c.QueueIndex == tracking && c.Field == "m_unitsElemId").ToList();
        if (ids.Count != 1 || ids[0].Integer is <= 0 or > uint.MaxValue) return null;
        long forgeCount = Count(walk, storage, "m_storedForgeSchemas"), parameterCount = Count(walk, storage, "m_storedParameterSchemas");
        var pairs = walk.Records.Where(r => r.QueueIndex == storage).Select(r => r.Values).ToList();
        if (forgeCount < 0 || parameterCount < 0 || pairs.Count != forgeCount + parameterCount) return null;
        var units = Map(pairs.Take((int)forgeCount));
        var parameters = Map(pairs.Skip((int)forgeCount));
        return units == null || parameters == null ? null : new((uint)ids[0].Integer, units, parameters);
    }

    private static long Count(WideBlockWalker.Walk walk, int queueIndex, string field)
    {
        var counts = walk.Captures.Where(c => c.QueueIndex == queueIndex && c.Field == field && c.Key.EndsWith('#')).ToList();
        return counts.Count == 1 ? counts[0].Integer : -1;
    }

    private static TfBlockDeserializer.DocumentSchemaMap? Map(IEnumerable<Dictionary<string, object?>> pairs)
    {
        var entries = new List<TfBlockDeserializer.DocumentSchemaEntry>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            if (pair.GetValueOrDefault("first") is not string key || pair.GetValueOrDefault("second") is not string value || !keys.Add(key)) return null;
            entries.Add(new(key, value, 0, 0));
        }
        return new(0, 0, entries.ToArray());
    }

    /// <summary>The unit options of a UnitsElem block (<paramref name="records"/> are the records of ForgeTypeId, FormatOptions and
    /// AUnits objects in stored order): per format option the spec, symbol and unit ids (three ForgeTypeIds) and the FormatOptions
    /// fields, then the four AUnits integers. Null when the records do not have exactly that shape.</summary>
    internal static TfBlockDeserializer.UnitOptions? UnitOptions(
        IReadOnlyList<(string Class, Dictionary<string, object?> Values)> records)
    {
        var options = new List<TfBlockDeserializer.UnitFormatOption>();
        var specs = new HashSet<string>(StringComparer.Ordinal);
        var ids = new List<string>();
        Dictionary<string, object?>? units = null;
        foreach (var (name, values) in records)
        {
            switch (name)
            {
                case "ForgeTypeId":
                    if (values.GetValueOrDefault("m_typeId") is not string id) return null;
                    ids.Add(id);
                    break;
                case "FormatOptions":
                {
                    if (ids.Count != 3 || !specs.Add(ids[0])) return null;
                    if (values.GetValueOrDefault("m_accuracy") is not double accuracy || !double.IsFinite(accuracy) || accuracy < 0) return null;
                    byte Flag(string field) => values.GetValueOrDefault(field) is long v and (0 or 1) ? (byte)v : (byte)255;
                    byte[] flags = new[] { "m_bSuppressLeadingZeros", "m_bSuppressSpaces", "m_bSuppressTrailingZeros", "m_bUseDefault", "m_bUseGrouping", "m_bUsePlusPrefix" }
                        .Select(Flag).ToArray();
                    if (flags.Any(f => f == 255) || values.GetValueOrDefault("m_roundingMethod") is not long rounding) return null;
                    options.Add(new(ids[0], ids[1], ids[2], accuracy, (int)rounding, flags[0], flags[1], flags[2], flags[3], flags[4], flags[5]));
                    ids.Clear();
                    break;
                }
                case "AUnits":
                    if (units != null) return null;
                    units = values;
                    break;
            }
        }
        if (units == null || ids.Count != 0) return null;
        int Int(string field) => units.GetValueOrDefault(field) is long v ? (int)v : int.MinValue;
        int[] tail = new[] { "m_universalUnitSystem", "m_decimalSymbol", "m_digitGroupingSymbol", "m_digitGroupingAmount" }.Select(Int).ToArray();
        return tail.Any(v => v == int.MinValue) ? null
            : new TfBlockDeserializer.UnitOptions(options.ToArray(), tail[0], tail[1], tail[2], tail[3]);
    }
}
