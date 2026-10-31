namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record WallCutFaceHistoryLink(int FromRowIndex, int ReferenceId, int? ToRowIndex, string Outcome);
    internal sealed record WallCutFaceHistoryChain(int RowIndex, WallHistoryEntry History, WallTableNode TableNode,
        IReadOnlyList<WallCutFaceHistoryLink> Links, int TerminalRowIndex, IReadOnlyList<int> TerminalKeys);
    internal sealed record WallCutFaceHistoryGenerator(int PhaseIndex, int StepQueueIndex, ushort ClassId,
        int GeneratorId, int Flags, IReadOnlyList<WallCutFaceHistoryChain> Chains);
    internal sealed record OwnWallDebugOffCutFaceHistoryChains(int OwnerId, ushort ClassId, int MaxId,
        IReadOnlyList<WallCutFaceHistoryGenerator> Generators);

    /// <summary>History indirection of the cut helper: kinds 5/15/16 use key 1 as another face id within the same
    /// generator. getFaceHistForId selects the first matching row, including inactive rows. A per-start-row visited-id
    /// set stops before re-reading an id; missing ids keep the current keys. The current Face/id do not change with the
    /// resolved history keys. This consumes explicitly supplied active histories (debug mode off), not the runtime
    /// state or a completed planarity/loop/extrusion predicate.</summary>
    internal static OwnWallDebugOffCutFaceHistoryChains? ResolveOwnWallDebugOffCutFaceHistoryChains(
        OwnWallDebugOffActiveFaceHistories input, out string? failure)
    {
        failure = "cut-history-owner";
        if (input.OwnerId <= 0 || input.ClassId is not (3524 or 361) || input.MaxId < -1 || input.MaxId > 100_000) return null;
        var generators = new List<WallCutFaceHistoryGenerator>();
        string context = "cut-history";
        try
        {
            foreach (var generator in input.Generators)
            {
                context = $"cut-history-rows:{generator.StepQueueIndex}";
                var rows = generator.Visits;
                var firstById = new Dictionary<int, int>();
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row.RowIndex != i || row.History.Keys.Count != 3
                        || i > 0 && Compare(rows[i - 1].History.Keys, row.History.Keys) >= 0
                        || row.Outcome is not ("active" or "inactive-flag" or "null" or "out-of-range" or "not-face")) throw Fail;
                    firstById.TryAdd(row.History.Id, i); // native linear lookup's FIRST match
                }
                var chains = new List<WallCutFaceHistoryChain>();
                foreach (var row in rows.Where(r => r.Outcome == "active"))
                {
                    context = $"cut-history-face:{generator.StepQueueIndex}:{row.RowIndex}";
                    if (row.TableNode is not { ClassId: 1589 } face || face.Archive is not ("Wall" or "GRep")
                        || (face.Flags & (1u << 19)) == 0 || face.Tag != row.History.Id
                        || face.Tag < 0 || face.Tag > input.MaxId) throw Fail;
                    var seen = new HashSet<int> { row.History.Id };
                    var links = new List<WallCutFaceHistoryLink>();
                    int current = row.RowIndex;
                    while (rows[current].History.Keys[0] is 5 or 15 or 16)
                    {
                        int reference = rows[current].History.Keys[1];
                        if (!seen.Add(reference))
                        { links.Add(new(current, reference, null, "cycle")); break; }
                        if (!firstById.TryGetValue(reference, out int target))
                        { links.Add(new(current, reference, null, "missing-id")); break; }
                        links.Add(new(current, reference, target, "resolved")); current = target;
                    }
                    chains.Add(new(row.RowIndex, row.History, face, links, current, rows[current].History.Keys));
                }
                generators.Add(new(generator.PhaseIndex, generator.StepQueueIndex, generator.ClassId,
                    generator.GeneratorId, generator.Flags, chains));
            }
            failure = null; return new(input.OwnerId, input.ClassId, input.MaxId, generators);
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
