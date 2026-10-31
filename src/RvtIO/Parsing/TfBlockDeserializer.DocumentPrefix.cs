using System.Text;

namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    private static readonly UnicodeEncoding DocumentBuildUtf16 = new(false, false, true);
    internal sealed record DocumentDeferredObject(string Field, ushort ClassId, uint Handle);
    internal sealed record DocumentRootPrefix(int End, int? AppInfoManagerQueueIndex,
        DocumentDeferredObject[] DeferredObjects, int DevBranchId, int SyncVersion,
        Guid[] ExecutedUpgrades, string[] StoredByRevitBuild);

    /// <summary>Reads only the complete ADocument header at the start of an exact, already decompressed Global/Latest
    /// stream. Does not decode its deferred graph or choose the current units. End is the start of that graph.</summary>
    internal static DocumentRootPrefix? ExtractOwnDocumentRootPrefix(byte[] data)
    {
        if (data.Length < 2 || BitConverter.ToUInt16(data, 0) != 28 || !DocumentPrefixSchemasMatch())
            return null;
        var cursor = new Cursor { D = data, Pos = 2, End = data.Length, RootClassId = 28 };
        var queue = new List<DocumentDeferredObject>();
        int? managerIndex = null;
        int branch = 0, sync = 0;
        Guid[] upgrades = [];
        string[] builds = [];
        uint Count()
        {
            uint count = ReadU32(cursor);
            if (count == uint.MaxValue) return 0;
            if (count > 100_000) throw Fail;
            return count;
        }
        void Pointer(string field)
        {
            uint handle = ReadU32(cursor);
            if (handle == 0) return;
            if (!cursor.Take(2)) throw Fail;
            ushort cls = BitConverter.ToUInt16(data, cursor.Pos - 2);
            if (cls == 0) return; // unresolved external pointer: no deferred payload
            if (cls == 4 || !Schemas.ContainsKey(cls) || queue.Count >= 100_000) throw Fail;
            if (field == "m_pAppInfoManager")
            {
                if (cls != 338) throw Fail;
                managerIndex = queue.Count;
            }
            queue.Add(new(field, cls, handle));
        }
        try
        {
            foreach (var field in SerializationFields(28)!)
            {
                // These three missing references are bound to typed constructors,
                // not inferred from a successful empty collection.
                if (field.Name == "m_devBranchInfo")
                { branch = ReadI32(cursor); sync = ReadI32(cursor); }
                else if (field.Name == "m_executedUpgrades")
                {
                    uint count = Count();
                    if (count * 16L > cursor.End - cursor.Pos) throw Fail;
                    upgrades = new Guid[(int)count];
                    for (int index = 0; index < upgrades.Length; index++)
                    {
                        upgrades[index] = new Guid(data.AsSpan(cursor.Pos, 16));
                        cursor.Pos += 16;
                    }
                }
                else if (field.Name == "m_storedByRevitBuild")
                {
                    uint count = Count();
                    if (count * 4L > cursor.End - cursor.Pos) throw Fail;
                    builds = new string[(int)count];
                    for (int index = 0; index < builds.Length; index++)
                    {
                        uint length = ReadU32(cursor);
                        if (length > 10_000 || length * 2L > cursor.End - cursor.Pos) throw Fail;
                        builds[index] = DocumentBuildUtf16.GetString(data, cursor.Pos, (int)length * 2);
                        cursor.Pos += (int)length * 2;
                    }
                }
                else if (field.Mods == 81)
                {
                    uint count = Count();
                    if (count * 4L > cursor.End - cursor.Pos) throw Fail;
                    for (uint index = 0; index < count; index++) Pointer(field.Name);
                }
                else if (field.Type == 14 && field.Mods is 1 or 2) Pointer(field.Name);
                else if (field.Type == 14 && field.Mods is 0 or 3) ReadU32(cursor);
                else if (field.Type == 1)
                {
                    if (!cursor.Take(1) || data[cursor.Pos - 1] > 1) throw Fail;
                }
                else throw Fail;
            }
        }
        catch (DecodeFail) { return null; }
        catch (DecoderFallbackException) { return null; }
        return new(cursor.Pos, managerIndex, queue.ToArray(), branch, sync, upgrades, builds);
    }

    private static bool DocumentPrefixSchemasMatch()
    {
        if (!Schemas.TryGetValue(28, out var document) || document.Name != "ADocument" || document.Base != null
            || !Schemas.TryGetValue(29, out var branch) || branch.Name != "DevBranchInfo" || branch.Base != null
            || branch.Fields.Count != 2 || branch.Fields[0] is not { Name: "m_devBranchId", Type: 4, Mods: 0 }
            || branch.Fields[1] is not { Name: "m_syncVersion", Type: 4, Mods: 0 }
            || !Schemas.TryGetValue(30, out var guid) || guid.Name != "GUIDvalue" || guid.Base != null
            || guid.Fields.Count != 1 || guid.Fields[0] is not { Name: "m_guid", Type: 9, Mods: 0 }
            || !Schemas.TryGetValue(338, out var manager) || manager.Name != "AppInfoManager" || manager.Base != null
            || manager.Fields.Count != 2 || manager.Fields[0] is not { Name: "m_pADoc", Type: 14, Mods: 3 }
            || manager.Fields[1] is not { Name: "m_appInfoArr", Type: 14, Mods: 81 }) return false;
        (string Name, byte Type, byte Mods, int? Ref)[] expected =
        [
            ("m_elemTable", 14, 2, null), ("m_appInfoArr", 14, 81, null),
            ("m_oContentTable", 14, 2, null), ("m_pHostDocument", 14, 3, null),
            ("m_pAppInfoManager", 14, 2, null), ("m_pStyleSettings", 14, 2, null),
            ("m_pHistory", 14, 2, null), ("m_pSteelModelInfo", 14, 2, null),
            ("m_pPartitionTable", 14, 2, null), ("m_oNobleSecondaryData", 14, 1, null),
            ("m_ownerFamilyId", 14, 0, 20), ("m_ownerFamilyContainingGroupId", 14, 0, 20),
            ("m_devBranchInfo", 14, 0, null), ("m_groupFile", 1, 0, null),
            ("m_corruptDocument", 1, 0, null), ("m_bIsCoreDocument", 1, 0, null),
            ("m_executedUpgrades", 14, 80, null), ("m_storedByRevitBuild", 14, 80, null),
            ("m_oExServicesUsed", 14, 2, null),
        ];
        return document.Fields.Count == expected.Length && document.Fields.Zip(expected).All(pair =>
            pair.First.Name == pair.Second.Name && pair.First.Type == pair.Second.Type
            && pair.First.Mods == pair.Second.Mods && pair.First.Ref == pair.Second.Ref);
    }
}
