using System.Globalization;

namespace RvtIO.Ifc;

public partial class IfcExporter
{
    /// <summary>The current profile names each IfcRelAssociatesMaterial after the first object it relates:
    /// <c>MD5("{project GlobalId}IfcRelAssociatesMaterial:{GlobalId of the first related object}")</c>.
    /// The first object is the one created first (lowest handle): a type is created together with its first instance and
    /// before it, so it counts just ahead of that instance. Relations whose members cannot be read keep their GlobalId.</summary>
    internal static int ApplyCurrentMaterialRelationGlobalIds(StepWriter w)
    {
        var relations = new List<(int Index, string[] Args, List<int> Related)>();
        var firstMemberOfType = new Dictionary<int, int>();
        string? projectGlobalId = null;
        for (int index = 0; index < w.EntityCount; index++)
        {
            var (_, type, args) = w.EntityAt(index);
            switch (type)
            {
                case "IFCRELASSOCIATESMATERIAL":
                    string[] relation = SplitStepArguments(args);
                    if (relation.Length == 6) relations.Add((index, relation, ReferenceIds(relation[4])));
                    break;
                case "IFCRELDEFINESBYTYPE":
                    string[] byType = SplitStepArguments(args);
                    if (byType.Length == 6 && ReferenceIds(byType[4]) is { Count: > 0 } members
                        && ReferenceIds(byType[5]) is { Count: 1 } typeIds)
                        firstMemberOfType.TryAdd(typeIds[0], members[0]);
                    break;
                case "IFCPROJECT" when projectGlobalId == null:
                    projectGlobalId = FirstString(args);
                    break;
            }
        }
        if (projectGlobalId == null || relations.Count == 0) return 0;

        var needed = relations.SelectMany(relation => relation.Related).ToHashSet();
        var globalIds = new Dictionary<int, string>();
        for (int index = 0; index < w.EntityCount; index++)
        {
            var (id, _, args) = w.EntityAt(index);
            if (needed.Contains(id) && FirstString(args) is { } globalId)
                globalIds[id] = globalId;
        }

        int rewritten = 0;
        foreach (var (index, args, related) in relations)
        {
            var known = related.Where(globalIds.ContainsKey).ToList();
            if (known.Count != related.Count || known.Count == 0) continue;
            int first = known.MinBy(id => firstMemberOfType.TryGetValue(id, out int member) ? member - 0.5 : id);
            args[0] = S(IfcGuid.FromMd5($"{projectGlobalId}IfcRelAssociatesMaterial:{globalIds[first]}"));
            w.ReplaceArgs(index, string.Join(",", args));
            rewritten++;
        }
        return rewritten;
    }

    private static List<int> ReferenceIds(string list) =>
        list.Trim('(', ')').Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => token.StartsWith('#') && int.TryParse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .Select(token => int.Parse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture)).ToList();

    /// <summary>The first attribute when it is a quoted string (the GlobalId of a rooted entity).</summary>
    private static string? FirstString(string args)
    {
        if (args.Length < 2 || args[0] != '\'') return null;
        int end = args.IndexOf('\'', 1);
        return end < 0 ? null : args[1..end];
    }
}
