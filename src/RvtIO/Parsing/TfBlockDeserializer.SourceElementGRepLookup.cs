namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    // RecordIndex is a caller-local identity token, not an ElementId, a native
    // address or an identity to reuse across archives. All runtime states below
    // are explicit inputs; persisted bytes are not assumed to contain them.
    internal sealed record SourceElementCell(int ElementId, int RecordIndex);
    internal sealed record SourceElementGRepCache(int RecordIndex, bool HasGRep, bool GRepExpanded);
    internal sealed record SourceElementGRepLookupPlan(string Outcome, int? RecordIndex,
        bool DeleteHashEntry, bool AddHashEntry, bool CallElemRecGetGRep,
        bool RequiresAlternateBackend, bool CurrentCellAuthorityClosed);

    /// <summary>Regular lookup ordering plus a signed exact tree lookup. A cached non-null GRep wins before the cell
    /// lookup; an expanded null also stops. A cold lookup requires the exact key, not the closest or lower-bound neighbour.
    /// The alternate virtual 3 is reported, not replaced by the regular algorithm.
    /// This is an explicit-state plan, not the actual cell or runtime state.</summary>
    internal static SourceElementGRepLookupPlan? PlanSourceElementGRepLookup(int elementId,
        bool alternateBackend, IReadOnlyList<SourceElementCell>? cells,
        SourceElementGRepCache? cache, out string? failure)
    {
        failure = null;
        if (elementId < 0) return new("negative-id-null", null, false, false, false, false, false);
        if (alternateBackend) return new("alternate-virtual3-required", null, false, false, false, true, false);
        failure = "source-grep-cache-identity";
        if (cache?.RecordIndex < 0) return null;
        failure = null;
        if (cache?.HasGRep == true) return new("cached-grep", cache.RecordIndex, false, false, false, false, false);
        if (cache?.GRepExpanded == true) return new("cached-expanded-null", null, false, false, false, false, false);
        failure = "source-grep-cell-order";
        if (cells != null)
        {
            var identities = new HashSet<int>();
            for (int i = 0; i < cells.Count; i++)
                if (cells[i].RecordIndex < 0 || !identities.Add(cells[i].RecordIndex)
                    || (i > 0 && cells[i - 1].ElementId >= cells[i].ElementId)) return null;
        }
        // lower_bound with a signed compare, then equality admission. The native
        // RB-tree storage is represented only by its caller-supplied ordered
        // unique keys; corrupt trees are not repaired or sorted.
        int lo = 0, hi = cells?.Count ?? 0;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (cells![mid].ElementId < elementId) lo = mid + 1; else hi = mid;
        }
        failure = null;
        if (cells == null || lo == cells.Count || cells[lo].ElementId != elementId)
            return new("no-exact-cell-null", null, cache != null, false, false, false, false);
        var current = cells[lo]; bool replace = cache == null || cache.RecordIndex != current.RecordIndex;
        return new("selected-cell-expansion-required", current.RecordIndex,
            cache != null && replace, replace, true, false, false);
    }
}
