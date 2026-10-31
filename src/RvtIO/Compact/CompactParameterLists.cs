using System.Globalization;

namespace RvtIO.Compact;

internal interface ICompactListFacts
{
    /// <summary>Integer value of a named fact (see <see cref="CompactParameterListRules.Facts"/>); null when the reader does not
    /// know it for this element. Ids use the stored encoding (-1 is the null id).</summary>
    int? Get(string fact);

    /// <summary>Built-in ids held by the element's parameter stores; null when the stores were not decoded exactly.</summary>
    IReadOnlyCollection<int>? StoredBuiltinIds { get; }

    /// <summary>Built-in ids of the FamilyParams items of the element or of its master symbol (see <c>ElementListFacts</c>); null when unknown.</summary>
    IReadOnlyCollection<int>? FamilyParameterIds(bool symbol, bool instanceItems, bool otherItems) => null;
}

/// <summary>The ascending built-in parameter ids of an element, or the reason they are not known.</summary>
internal readonly record struct CompactListResult(int[]? Ids, string? Unknown);

internal static class CompactParameterLists
{
    private sealed record NodeRow(int Fact, int[] Upper, int[] Child);

    private sealed record ClassRow(int[] Groups, HashSet<int> Removable, int Exact, int Total);

    private static readonly NodeRow?[] Nodes = ParseNodes();
    private enum GroupKind { Ids, Unmodelled, Stored, FamilyParams }

    private static readonly (GroupKind Kind, int[] Ids, int Root)[] Groups = ParseGroups();
    // "P:<self|symbol>:<instance items><other items>" rows keep their selector here, by group index
    private static readonly Dictionary<int, (bool Symbol, bool Instance, bool Other)> FamilyParamSelectors = ParseFamilyParamSelectors();
    private static readonly Dictionary<string, ClassRow> Classes = BuildClasses();
    private static readonly HashSet<int> StoredListed = [.. CompactParameterListRules.StoredListed];
    private static readonly HashSet<int> StoredUnlisted = [.. CompactParameterListRules.StoredUnlisted];
    private static readonly int[] ObservedCategoriesWithPhases =
    [
        -2009030, -2008234, -2008128, -2008126, -2008099, -2008087, -2008083, -2008055, -2008049, -2008013, -2008010, -2001140,
        -2001060, -2000126, -2000100, -2000035, -2000032, -2000011,
    ];

    private static readonly HashSet<int> CategoriesWithPhases = [.. CompactParameterListRules.CategoriesWithPhases, .. ObservedCategoriesWithPhases];
    private static readonly HashSet<int> CategoriesWithoutPhases = [.. CompactParameterListRules.CategoriesWithoutPhases];

    /// <summary>The id the stored-parameter pass skips: the type mark is listed by the category rules only.</summary>
    private const int TypeMarkParameter = -1001405;

    internal static bool IsSupported(string recordClass) => Classes.ContainsKey(recordClass);

    internal static (int Exact, int Total)? Source(string recordClass) =>
        Classes.TryGetValue(recordClass, out var row) ? (row.Exact, row.Total) : null;

    /// <summary>Whether the phase parameters exist for elements of a built-in category (null: category not validated).</summary>
    internal static bool? CategoryHasPhases(int category) =>
        CategoriesWithPhases.Contains(category) ? true : CategoriesWithoutPhases.Contains(category) ? false : null;

    internal static bool? StoredIdIsListed(int id) =>
        StoredListed.Contains(id) ? true : StoredUnlisted.Contains(id) ? false : null;

    internal static CompactListResult GetBuiltinIds(string recordClass, ICompactListFacts facts)
    {
        if (!Classes.TryGetValue(recordClass, out var cls)) return CompactObservedLists.GetBuiltinIds(recordClass, facts);
        var ids = new SortedSet<int>();
        var removed = new HashSet<int>();   // ids a contribution takes out again, after the stored pass
        bool collectsStored = false;   // a class without a "stored" row never reads its stored parameters
        foreach (int g in cls.Groups)
        {
            var (kind, groupIds, root) = Groups[g];
            bool? holds = Evaluate(root, facts, out string? missing);
            if (kind == GroupKind.Unmodelled)
            {
                // a contribution the rules do not model (the framing user data is modelled for the members it is decided for)
                if (holds == true && IsFramingUserData(root) && CompactFramingUserData.Contribute(facts, ids, removed)) continue;
                if (holds != false) return new CompactListResult(null, "unmodelled contribution" + (missing is null ? " (group " + g.ToString(CultureInfo.InvariantCulture) + ")" : " (" + missing + ")"));
                continue;
            }
            if (holds is null) return new CompactListResult(null, "fact " + missing);
            if (kind == GroupKind.FamilyParams)
            {
                if (holds != true) continue;
                var (symbol, instance, other) = FamilyParamSelectors[g];
                var items = facts.FamilyParameterIds(symbol, instance, other);
                if (items is null) return new CompactListResult(null, "family parameter items unknown");
                // an item the class code may remove again stays or goes by a condition this path does not evaluate
                if (items.Any(cls.Removable.Contains)) return new CompactListResult(null, "family parameter item that the class may remove");
                foreach (int id in items) ids.Add(id);
                continue;
            }
            if (kind == GroupKind.Stored) collectsStored = holds == true;
            else if (holds == true) foreach (int id in groupIds) ids.Add(id);
        }
        if (!collectsStored) return new CompactListResult([.. ids.Except(removed)], null);
        var stored = facts.StoredBuiltinIds;
        if (stored is null) return new CompactListResult(null, "stored parameter ids unknown");
        foreach (int id in stored)
        {
            if (id >= 0 || id == TypeMarkParameter || cls.Removable.Contains(id)) continue;
            bool? listed = StoredIdIsListed(id);
            if (listed is null) return new CompactListResult(null, "stored id " + id.ToString(CultureInfo.InvariantCulture) + " not validated");
            if (listed == true) ids.Add(id);
        }
        return new CompactListResult([.. ids.Except(removed)], null);
    }

    /// <summary>True for the group whose condition is the user data of a family instance (hasUserData and its kind).</summary>
    private static bool IsFramingUserData(int root) =>
        root > 1 && CompactParameterListRules.Facts[Nodes[root]!.Fact] == "fi:hasUserData";

    /// <summary>Truth of a decision diagram; null when the unknown facts leave both outcomes possible.</summary>
    private static bool? Evaluate(int node, ICompactListFacts facts, out string? missing)
    {
        missing = null;
        if (node <= 1) return node == 1;
        var row = Nodes[node]!;
        string name = CompactParameterListRules.Facts[row.Fact];
        int? value = facts.Get(name);
        if (value is int v)
        {
            for (int i = 0; i < row.Upper.Length; i++)
                if (row.Upper[i] == int.MaxValue || v <= row.Upper[i])
                    return Evaluate(row.Child[i], facts, out missing);
            return Evaluate(row.Child[^1], facts, out missing);
        }
        bool? first = null;
        for (int i = 0; i < row.Child.Length; i++)
        {
            bool? r = Evaluate(row.Child[i], facts, out string? inner);
            if (r is null) { missing = inner ?? name; return null; }
            if (first is null) first = r;
            else if (first != r) { missing = name; return null; }
        }
        return first;
    }

    private static NodeRow?[] ParseNodes()
    {
        var rows = new NodeRow?[CompactParameterListRules.Nodes.Length];
        for (int i = 2; i < rows.Length; i++)
        {
            string[] parts = CompactParameterListRules.Nodes[i].Split(';');
            int fact = int.Parse(parts[0], CultureInfo.InvariantCulture);
            var upper = new int[parts.Length - 1];
            var child = new int[parts.Length - 1];
            for (int k = 1; k < parts.Length; k++)
            {
                int colon = parts[k].IndexOf(':');
                string hi = parts[k][..colon];
                upper[k - 1] = hi == "*" ? int.MaxValue : int.Parse(hi, CultureInfo.InvariantCulture);
                child[k - 1] = int.Parse(parts[k][(colon + 1)..], CultureInfo.InvariantCulture);
            }
            rows[i] = new NodeRow(fact, upper, child);
        }
        return rows;
    }

    private static (GroupKind Kind, int[] Ids, int Root)[] ParseGroups()
    {
        var rows = new (GroupKind, int[], int)[CompactParameterListRules.Groups.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            string text = CompactParameterListRules.Groups[i];
            int bar = text.IndexOf('|');
            string idText = text[..bar];
            int root = int.Parse(text[(bar + 1)..], CultureInfo.InvariantCulture);
            rows[i] = idText switch
            {
                "U" => (GroupKind.Unmodelled, [], root),
                "S" => (GroupKind.Stored, [], root),
                _ when idText.StartsWith("P:", StringComparison.Ordinal) => (GroupKind.FamilyParams, [], root),
                _ => (GroupKind.Ids, idText.Split(',').Select(id => int.Parse(id, CultureInfo.InvariantCulture)).ToArray(), root),
            };
        }
        return rows;
    }

    private static Dictionary<int, (bool, bool, bool)> ParseFamilyParamSelectors()
    {
        var map = new Dictionary<int, (bool, bool, bool)>();
        for (int i = 0; i < CompactParameterListRules.Groups.Length; i++)
        {
            string text = CompactParameterListRules.Groups[i];
            if (!text.StartsWith("P:", StringComparison.Ordinal)) continue;
            string[] parts = text[..text.IndexOf('|')].Split(':');
            map[i] = (parts[1] == "symbol", parts[2][0] == '1', parts[2][1] == '1');
        }
        return map;
    }

    private static Dictionary<string, ClassRow> BuildClasses()
    {
        var map = new Dictionary<string, ClassRow>(StringComparer.Ordinal);
        foreach (var (name, groups, removable, exact, total) in CompactParameterListRules.Classes)
            map[name] = new ClassRow(groups, [.. removable], exact, total);
        return map;
    }
}
