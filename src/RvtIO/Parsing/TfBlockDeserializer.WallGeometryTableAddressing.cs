namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record WallLogicalGeomSlot(int Id, int GeneratorId, string Storage,
        int? StorageQueueIndex, int StorageIndex, int? Start, int? End);
    internal sealed record OwnWallGeometryTableAddressing(int OwnerId, int TableQueueIndex,
        int MaxId, int MaxSafeTag, uint SmallCount, int? BigQueueIndex, int? StartIndex,
        int? FilledBase, uint? BigCount, uint? FilledCount, IReadOnlyList<WallLogicalGeomSlot> Slots);

    /// <summary>GeomTable.getMaxId/getEntry logical addressing of persisted generator ids. Big prefixes use filled
    /// buckets (null bucket id -2); the tail uses the big array when non-empty, otherwise the small array shifted by
    /// start. maxSafeTag is kept, not a bound. Runtime GNode pointers, callback updates, current faces and active
    /// geometry are not inferred from these persisted ids.</summary>
    internal static OwnWallGeometryTableAddressing? SelectOwnWallGeometryTableAddressing(
        OwnWallCompleteInputs input, out string? failure)
    {
        failure = "table-owner";
        var history = input.Histories.Histories;
        var pointer = history.GeometryTablePointer;
        var tables = history.GeometryTables.Where(t => t.QueueIndex == pointer.QueueIndex).ToArray();
        if (history.List.OwnerId <= 0 || pointer is not { Handle: uint.MaxValue, ClassId: 2087 }
            || tables.Length != 1 || !HasOwnWallGeometryTableSchema() || !HasCompleteWallInputSchemas()) return null;
        var table = tables[0];
        failure = "small-count";
        if (table.EntryCount == uint.MaxValue || table.EntryCount != table.Entries.Count
            || table.Entries.Where((e, i) => e.Index != i).Any()) return null;
        if (input.LaterObjects.Select(n => n.QueueIndex).Distinct().Count() != input.LaterObjects.Count) return null;
        var nodes = input.LaterObjects.ToDictionary(n => n.QueueIndex);
        var bigPointer = table.BigTableOwner;
        try
        {
            int start = 0, filledBase = 0;
            WallCompleteInputObject? big = null;
            WallCompleteInputField? bigField = null;
            uint? bigCount = null, filledCount = null;
            int[] bigIds = [];
            IReadOnlyList<int?> buckets = [];
            if (bigPointer.Handle != 0)
            {
                failure = "big-owner";
                if (bigPointer.ClassId != 532 || bigPointer.QueueIndex is not int index
                    || !nodes.TryGetValue(index, out big) || big.ClassId != 532) return null;
                failure = "big-fields";
                start = Scalar(big, "m_startIndex"); filledBase = Scalar(big, "m_filledBase");
                bigField = Field(big, "m_bigTable"); bigIds = Ids(bigField); bigCount = (uint)bigIds.Length;
                var filled = Field(big, "m_filledTables"); buckets = Buckets(filled); filledCount = (uint)buckets.Count;
                if (start < 0 || start > 100_000 || start > 0 && filledBase <= 1) throw Fail;
            }
            failure = "logical-size";
            long length = (long)start + (bigIds.Length > 0 ? bigIds.Length : table.Entries.Count);
            if (length > 100_000) return null;
            var slots = new List<WallLogicalGeomSlot>();
            for (int id = 0; id < length; id++)
            {
                failure = $"slot:{id}";
                if (big != null && id < start)
                {
                    int bucket = id / filledBase, offset = id % filledBase;
                    if (bucket >= buckets.Count) throw Fail;
                    if (buckets[bucket] is not int chunkIndex)
                    { slots.Add(new(id, -2, "Default", null, offset, null, null)); continue; }
                    if (!nodes.TryGetValue(chunkIndex, out var chunk) || chunk.ClassId != 1859) throw Fail;
                    var field = Field(chunk, "m_table"); int[] ids = Ids(field);
                    if (offset >= ids.Length) throw Fail;
                    slots.Add(Slot(id, ids[offset], "Filled", chunkIndex, offset, field.Start));
                }
                else if (bigIds.Length > 0)
                {
                    int offset = id - start;
                    slots.Add(Slot(id, bigIds[offset], "Big", big!.QueueIndex, offset, bigField!.Start));
                }
                else
                {
                    var entry = table.Entries[id - start];
                    slots.Add(new(id, entry.GeneratorId, "Small", table.QueueIndex, entry.Index, entry.Start, entry.End));
                }
            }
            failure = null;
            return new(history.List.OwnerId, table.QueueIndex, (int)length - 1, table.MaxSafeTag,
                table.EntryCount, big?.QueueIndex, big == null ? null : start,
                big == null ? null : filledBase, bigCount, filledCount, slots);
        }
        catch (DecodeFail) { return null; }

        static WallLogicalGeomSlot Slot(int id, int generator, string storage, int queueIndex, int offset, int fieldStart)
            => new(id, generator, storage, queueIndex, offset, fieldStart + 4 + offset * 4, fieldStart + 8 + offset * 4);

        static WallCompleteInputField Field(WallCompleteInputObject node, string name)
        {
            var fields = node.Fields.Where(f => f.Declaring == node.ClassId && f.Name == name).ToArray();
            if (fields.Length != 1 || fields[0].End - fields[0].Start != fields[0].Bytes.Length) throw Fail;
            return fields[0];
        }
        static int Scalar(WallCompleteInputObject node, string name)
        {
            var field = Field(node, name); if (field.Bytes.Length != 4 || field.QueuedObjects.Count != 0) throw Fail;
            return BitConverter.ToInt32(field.Bytes);
        }
        static int[] Ids(WallCompleteInputField field)
        {
            if (field.Bytes.Length < 4 || field.QueuedObjects.Count != 0) throw Fail;
            uint count = BitConverter.ToUInt32(field.Bytes);
            // A serialized null-count sentinel is preserved by the input reader;
            // it is not replaced with an empty array.
            if (count > 100_000 || 4L + count * 4L != field.Bytes.Length) throw Fail;
            return Enumerable.Range(0, (int)count).Select(i => BitConverter.ToInt32(field.Bytes, 4 + i * 4)).ToArray();
        }
        static IReadOnlyList<int?> Buckets(WallCompleteInputField field)
        {
            var c = new Cursor { D = field.Bytes, End = field.Bytes.Length };
            uint count = ReadU32(c); if (count > 100_000) throw Fail;
            var values = new List<int?>(); int queued = 0;
            for (uint i = 0; i < count; i++)
            {
                uint handle = ReadU32(c);
                if (handle == 0) { values.Add(null); continue; }
                if (!c.Take(2) || BitConverter.ToUInt16(c.D, c.Pos - 2) != 1859
                    || queued >= field.QueuedObjects.Count) throw Fail;
                values.Add(field.QueuedObjects[queued++]);
            }
            if (c.Pos != c.End || queued != field.QueuedObjects.Count) throw Fail;
            return values;
        }
    }
}
