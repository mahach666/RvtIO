using System.Text;

namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    private static readonly UnicodeEncoding DocumentAuthorUtf16 = new(false, false, true);
    internal sealed record DocumentEpisodeCount(int EpisodeId, int Count);
    internal sealed record DocumentContentRecord(int Start, int End, Guid Key, string Author,
        int OriginalElementId, int CreationDate, int LastModificationDate,
        int LastUserModificationDate, DocumentEpisodeCount[] EpisodeCounts);
    internal sealed record DocumentContentTable(int QueueIndex, int Start, int End,
        DocumentContentRecord[] Records);
    internal sealed record DocumentOwnedAppInfo(int ArrayIndex, int QueueIndex, ushort ClassId, uint Handle);
    internal sealed record DocumentManagerPrefix(DocumentRootPrefix Header, int ManagerStart, int End, int AppInfoArrayCount,
        DocumentContentTable[] ContentTables, DocumentOwnedAppInfo[] OwnedAppInfos,
        int UnitsTrackingQueueIndex);

    /// <summary>Reads the FIFO prefix through the named AppInfoManager and locates its unique owned UnitsTracking.
    /// Does not read that object's payload, select a UnitsElem or accept the rest of the Global graph.
    /// Only source-bound records before the manager are supported.</summary>
    internal static DocumentManagerPrefix? ExtractOwnDocumentManagerPrefix(byte[] data)
    {
        var header = ExtractOwnDocumentRootPrefix(data);
        if (header?.AppInfoManagerQueueIndex is not int managerIndex || !DocumentManagerSchemasMatch())
            return null;
        var cursor = new Cursor { D = data, Pos = header.End, End = data.Length, RootClassId = 28 };
        var tables = new List<DocumentContentTable>();
        var appInfos = new List<DocumentOwnedAppInfo>();
        int? unitsIndex = null;
        uint Count(int minimumSize)
        {
            uint count = ReadU32(cursor);
            if (count == uint.MaxValue) return 0;
            if (count > 100_000 || count * (long)minimumSize > cursor.End - cursor.Pos) throw Fail;
            return count;
        }
        void DocumentBackReference()
        {
            // The stream's root is the implicit handle 1, not a model element id.
            if (ReadU32(cursor) != 1) throw Fail;
        }
        try
        {
            for (int index = 0; index < managerIndex; index++)
            {
                var queued = header.DeferredObjects[index];
                if (queued.ClassId == 821 && queued.Field == "m_oContentTable")
                {
                    int start = cursor.Pos;
                    DocumentBackReference();
                    uint count = Count(44);
                    var records = new DocumentContentRecord[(int)count];
                    for (int recordIndex = 0; recordIndex < records.Length; recordIndex++)
                    {
                        int recordStart = cursor.Pos;
                        DocumentBackReference();
                        if (!cursor.Take(16)) throw Fail;
                        Guid key = new(data.AsSpan(cursor.Pos - 16, 16));
                        uint length = ReadU32(cursor);
                        if (length > 10_000 || length * 2L > cursor.End - cursor.Pos) throw Fail;
                        string author = DocumentAuthorUtf16.GetString(data, cursor.Pos, (int)length * 2);
                        cursor.Pos += (int)length * 2;
                        int original = ReadI32(cursor), creation = ReadI32(cursor);
                        int modification = ReadI32(cursor), userModification = ReadI32(cursor);
                        uint episodes = Count(8);
                        var counts = new DocumentEpisodeCount[(int)episodes];
                        for (int episode = 0; episode < counts.Length; episode++)
                            counts[episode] = new(ReadI32(cursor), ReadI32(cursor));
                        records[recordIndex] = new(recordStart, cursor.Pos, key, author, original,
                            creation, modification, userModification, counts);
                    }
                    tables.Add(new(index, start, cursor.Pos, records));
                }
                else if (queued.Field == "m_appInfoArr" && queued.ClassId is 27 or 4030)
                {
                    DocumentBackReference();
                    if (queued.ClassId == 4030) ReadI32(cursor); // legacy, not owned by this manager
                }
                else throw Fail; // no scan, guessed widths or partial skip
            }
            int managerStart = cursor.Pos;
            DocumentBackReference();
            uint appInfoCount = Count(4);
            for (int arrayIndex = 0; arrayIndex < appInfoCount; arrayIndex++)
            {
                uint handle = ReadU32(cursor);
                if (handle == 0) continue;
                if (!cursor.Take(2)) throw Fail;
                ushort cls = BitConverter.ToUInt16(data, cursor.Pos - 2);
                if (cls == 0) continue; // unresolved external; no owned deferred payload
                if (!IsSubclassOf(cls, 27) || header.DeferredObjects.Length + appInfos.Count >= 100_000)
                    throw Fail;
                int queueIndex = header.DeferredObjects.Length + appInfos.Count;
                appInfos.Add(new(arrayIndex, queueIndex, cls, handle));
                if (cls == 4030)
                {
                    if (unitsIndex.HasValue) throw Fail;
                    unitsIndex = queueIndex;
                }
            }
            return unitsIndex.HasValue ? new(header, managerStart, cursor.Pos, (int)appInfoCount, tables.ToArray(),
                appInfos.ToArray(), unitsIndex.Value) : null;
        }
        catch (DecodeFail) { return null; }
        catch (DecoderFallbackException) { return null; }
    }

    private static bool DocumentManagerSchemasMatch()
    {
        bool Match(int id, string name, int? baseId, params (string Name, byte Type, byte Mods, int? Ref)[] expected)
            => Schemas.TryGetValue(id, out var schema) && schema.Name == name && schema.Base == baseId
                && schema.Fields.Count == expected.Length && schema.Fields.Zip(expected).All(pair =>
                    pair.First.Name == pair.Second.Name && pair.First.Type == pair.Second.Type
                    && pair.First.Mods == pair.Second.Mods && pair.First.Ref == pair.Second.Ref);
        // These guards describe the stored schema. Missing or wrong recovered references (history, episode pair,
        // last-user date) are corrected locally with typed constructors, never by rewriting the shared schema dictionary.
        return Match(821, "ContentTable", null, ("m_pHostDocument", 14, 3, null), ("m_ContentRecSet", 14, 80, 818))
            && Match(818, "ContentRec", null, ("m_pHostDocument", 14, 3, null), ("m_ContentKey", 14, 0, 816),
                ("m_author", 8, 96, null), ("m_history", 14, 0, null), ("m_EpisodeCounts", 14, 80, null))
            && Match(816, "ContentKey", null, ("m_guidKey", 14, 0, 30))
            && Match(819, "ElementHistory", null, ("m_originalElementId", 14, 0, 546),
                ("m_creationDate", 14, 0, 504), ("m_lastModificationDate", 14, 0, 504),
                ("m_lastUserModificationDate", 14, 0, 819))
            && Match(504, "EpisodeId", null, ("m_id", 4, 0, 504))
            && Match(546, "OriginalElementId", null, ("m_id", 4, 0, 546))
            && Match(820, "std::pair< EpisodeId, int >", null, ("first", 14, 0, 504), ("second", 4, 0, 820))
            && Match(27, "AppInfo", null, ("m_pADoc", 14, 3, null))
            && Match(4030, "UnitsTracking", 27, ("m_unitsElemId", 14, 0, 20));
    }
}
