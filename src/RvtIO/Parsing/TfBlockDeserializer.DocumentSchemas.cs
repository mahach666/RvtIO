namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record DocumentSchemaEntry(string Key, string Value, int Start, int End);
    internal sealed record DocumentSchemaMap(int Start, int End, DocumentSchemaEntry[] Entries);
    internal sealed record DocumentSchemaPrefix(int QueueIndex, int Start, int End,
        DocumentSchemaMap Units, DocumentSchemaMap Parameters,
        DocumentDeferredObject[] DeferredObjects, DocumentGraphObject[] Objects, DocumentGraphField[] Fields);

    /// <summary>Own named-manager ESSchemaStorage through the two complete persisted AString maps. Values are verbatim
    /// JSON text, not evaluated unit settings. The remaining storage fields and the Global suffix are not accepted.</summary>
    internal static DocumentSchemaPrefix? ExtractOwnDocumentSchemas(byte[] data, out string? failure)
    {
        failure = "Unsupported header, manager or persisted schema fields";
        var manager = ExtractOwnDocumentManagerPrefix(data);
        if (manager == null || !Schemas.TryGetValue(1238, out var schema)
            || schema.Name != "ESSchemaStorage" || schema.Base != 27 || schema.Fields.Count != 4
            || schema.Fields[0] is not { Name: "m_storedForgeSchemas", Type: 14, Mods: 80, Ref: null }
            || schema.Fields[1] is not { Name: "m_storedParameterSchemas", Type: 14, Mods: 80, Ref: 1239 }
            || !Schemas.TryGetValue(1239, out var pair) || pair.Name != "std::pair< AString, AString >"
            || pair.Base != null || pair.Fields.Count != 2
            || pair.Fields[0] is not { Name: "first", Type: 8, Mods: 96, Ref: null }
            || pair.Fields[1] is not { Name: "second", Type: 8, Mods: 96, Ref: 1239 }) return null;
        var owners = manager.OwnedAppInfos.Where(x => x.ClassId == 1238).ToArray();
        if (owners.Length != 1) { failure = "Missing or ambiguous manager-owned ESSchemaStorage"; return null; }
        int index = owners[0].QueueIndex;
        var graph = ReadOwnDocumentGraphPrefix(data, manager, index, out failure);
        if (graph == null) return null;
        var cursor = new Cursor { D = data, Pos = graph.End, End = data.Length, RootClassId = 28 };
        string String()
        {
            uint length = ReadU32(cursor);
            if (length > 10_000 || length * 2L > cursor.End - cursor.Pos) throw Fail;
            string value = DocumentGraphUtf16.GetString(data, cursor.Pos, (int)length * 2);
            cursor.Pos += (int)length * 2; return value;
        }
        DocumentSchemaMap Map()
        {
            int start = cursor.Pos; uint count = ReadU32(cursor);
            if (count == uint.MaxValue) count = 0;
            if (count > 100_000 || count * 8L > cursor.End - cursor.Pos) throw Fail;
            var entries = new DocumentSchemaEntry[(int)count]; var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int n = 0; n < entries.Length; n++)
            {
                int recordStart = cursor.Pos; string key = String(); string value = String();
                if (!keys.Add(key)) throw Fail;
                entries[n] = new(key, value, recordStart, cursor.Pos);
            }
            return new(start, cursor.Pos, entries);
        }
        try
        {
            if (ReadU32(cursor) != 1) throw Fail;
            var units = Map(); var parameters = Map(); failure = null;
            return new(index, graph.End, cursor.Pos, units, parameters,
                graph.DeferredObjects, graph.Objects, graph.Fields);
        }
        catch (DecodeFail) { failure = $"Malformed own schema map at {cursor.Pos}"; return null; }
        catch (System.Text.DecoderFallbackException) { failure = $"Invalid own schema UTF16 at {cursor.Pos}"; return null; }
    }
}
