namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record WallActiveFaceHistoryVisit(int RowIndex, WallHistoryEntry History,
        WallTableNode? TableNode, string Outcome);
    internal sealed record WallActiveFaceHistoryGenerator(int PhaseIndex, int StepQueueIndex,
        ushort ClassId, int GeneratorId, int Flags, IReadOnlyList<WallActiveFaceHistoryVisit> Visits);
    internal sealed record OwnWallDebugOffActiveFaceHistories(int OwnerId, ushort ClassId, int MaxId,
        IReadOnlyList<WallActiveFaceHistoryGenerator> Generators);

    /// <summary>ActiveFaceTableIter (no history-pattern filter) over an explicitly supplied captured, cleared table
    /// (debug mode off). Each history id must select a non-null runtime Face with flag 19; the generator-id column and
    /// the serialized face identity are not extra predicates. All row outcomes are kept. Requires strictly
    /// signed-key-sorted unique histories; an unconfirmed normalization is not guessed. Does not establish the runtime
    /// context, pointer aliasing, extrusion validity or the default IFC integration.</summary>
    internal static OwnWallDebugOffActiveFaceHistories? SelectOwnWallDebugOffActiveFaceHistories(
        OwnWallCompleteInputs input, OwnWallDebugOffClearedTable table, out string? failure)
    {
        var history = input.Histories.Histories; var list = history.List;
        failure = "active-history-owner";
        if (list.OwnerId <= 0 || list.OwnerId != table.OwnerId || list.ClassId != table.ClassId
            || list.ClassId is not (3524 or 361) || list.OwnerReference != 2 || !history.Complete || list.Phases.Count != 6
            || table.MaxId < -1 || table.MaxId > 100_000 || table.Slots.Count != table.MaxId + 1
            || table.Slots.Where((s, i) => s.Persisted.Id != i || s.Node != null && s.Node.Tag != i).Any()) return null;
        var steps = history.Steps.ToDictionary(s => s.QueueIndex);
        var generators = new List<WallActiveFaceHistoryGenerator>();
        string context = "active-history";
        try
        {
            for (int phase = 0; phase < list.Phases.Count; phase++)
                foreach (var pointer in list.Phases[phase].Members)
                {
                    context = $"active-history-step:{phase}:{pointer.QueueIndex}";
                    if (pointer.QueueIndex is not int index || !steps.TryGetValue(index, out var step)
                        || step.ClassId != pointer.ClassId || step.OwnerReference != 2
                        || step.ClassId is not (150 or 365 or 500 or 590 or 596 or 677 or 977 or 982 or 1502 or 1621
                            or 2256 or 2262 or 3665 or 4036 or 4098 or 4137 or 4138 or 4147 or 4151 or 4153
                            or 4155 or 4165 or 4166 or 4167)) throw Fail;
                    var arrays = step.Tables.Where(t => t.Name == "m_faceHistTable").ToArray();
                    if (arrays.Length != 1) throw Fail;
                    var faces = arrays[0];
                    context = $"active-history-order:{index}";
                    if (faces.Count == uint.MaxValue || faces.Count != faces.Entries.Count
                        || faces.Entries.Any(e => e.Keys.Count != 3)) throw Fail;
                    for (int i = 1; i < faces.Entries.Count; i++)
                        if (Compare(faces.Entries[i - 1].Keys, faces.Entries[i].Keys) >= 0) throw Fail;
                    var visits = new List<WallActiveFaceHistoryVisit>();
                    for (int row = 0; row < faces.Entries.Count; row++)
                    {
                        var entry = faces.Entries[row]; var id = entry.Id;
                        WallTableNode? node = id >= 0 && id <= table.MaxId ? table.Slots[id].Node : null;
                        string outcome;
                        if (id < 0 || id > table.MaxId) outcome = "out-of-range";
                        else if (node == null) outcome = "null";
                        else
                        {
                            context = $"active-history-node:{index}:{row}:{id}";
                            if (node.Archive is not ("Wall" or "GRep")) throw Fail;
                            if (node.ClassId == 1589) outcome = (node.Flags & (1u << 19)) != 0 ? "active" : "inactive-flag";
                            else if (node.ClassId is 1736 or 1974 or 1243) outcome = "not-face";
                            else throw Fail; // unbound runtime kind is not guessed non-Face
                        }
                        visits.Add(new(row, entry, node, outcome));
                    }
                    // ActiveFaceTableIter does not skip step flags 0x100000,
                    // compare persisted generator ids or deduplicate table Faces.
                    generators.Add(new(phase, index, step.ClassId, step.Id, step.Flags, visits));
                }
            failure = null;
            return new(list.OwnerId, list.ClassId, table.MaxId, generators);
        }
        catch (DecodeFail) { failure = context; return null; }

        static int Compare(IReadOnlyList<int> left, IReadOnlyList<int> right)
        {
            for (int i = 0; i < 3; i++)
            { int result = left[i].CompareTo(right[i]); if (result != 0) return result; }
            return 0;
        }
    }
}
