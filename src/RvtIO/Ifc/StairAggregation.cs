using RvtIO.Internal;
using RvtIO.Model;

namespace RvtIO.Ifc;

/// <summary>Container semantics for the core members of a stair.
/// Admission happens after product emission, not from decoded geometry or bounding boxes.
/// Railing host links are independent of core ownership. Run/landing/support names use
/// numeric ordinals of the stair's component sets, including filtered members.</summary>
internal static class StairAggregation
{
    internal static void Apply(StepWriter writer, IReadOnlyList<Element> elements,
        IReadOnlyDictionary<int, (int entityId, int placementId, string ifcType)> products,
        Dictionary<int, List<int>> storeyContents, int ownerHistory, bool currentProfile = false)
    {
        var candidates = elements.Where(element => element.SerializedStairCore is { Length: > 0 }).ToArray();
        if (candidates.Length == 0) return;
        var counts = new Dictionary<int, int>();
        foreach (var element in elements) counts[element.Id] = counts.GetValueOrDefault(element.Id) + 1;
        var unique = elements.Where(element => counts[element.Id] == 1).ToDictionary(element => element.Id);
        var railingsByHost = unique.Values.Where(element => !element.IsBlockOnly && element.SerializedRailingStairHostId is > 0)
            .ToLookup(element => element.SerializedRailingStairHostId!.Value);
        var indexById = new Dictionary<int, int>(writer.EntityCount);
        var decomposed = new HashSet<int>();
        var existingParents = new HashSet<int>();
        for (int index = 0; index < writer.EntityCount; index++)
        {
            var entity = writer.EntityAt(index);
            indexById.Add(entity.Id, index);
            if (entity.Type is not ("IFCRELAGGREGATES" or "IFCRELNESTS")) continue;
            var args = IfcExporter.SplitStepArguments(entity.Args);
            if (args.Length != 6) continue;
            if (Id(args[4], out int parent)) existingParents.Add(parent);
            foreach (string child in List(args[5])) if (Id(child, out int id)) decomposed.Add(id);
        }
        foreach (var parent in candidates)
        {
            if (parent.Id <= 0 || parent.IsBlockOnly || parent.CategoryId != -2000120
                || parent.RuleClassName != "StairsElement" || !unique.ContainsKey(parent.Id)
                || !products.TryGetValue(parent.Id, out var emittedParent)
                || emittedParent.ifcType != "IFCSTAIR" || existingParents.Contains(emittedParent.entityId)
                || !indexById.TryGetValue(emittedParent.entityId, out int parentIndex)) continue;
            var parentEntity = writer.EntityAt(parentIndex);
            var parentArgs = IfcExporter.SplitStepArguments(parentEntity.Args);
            if (parentEntity.Type != "IFCSTAIR" || parentArgs.Length != 9
                || parentArgs[7] != StepWriter.Str(parent.Id.ToString())
                || parentArgs[0].Length != 24 || IfcGuid.Parse(parentArgs[0][1..^1]) == null) continue;
            var core = parent.SerializedStairCore!;
            if (core.Select(child => child.Id).Distinct().Count() != core.Length
                || core.Any(child => child.Id <= 0 || child.Id == parent.Id
                    || child.ClassId is not (3708 or 3686 or 3727)
                    || !unique.TryGetValue(child.Id, out var element) || element.IsBlockOnly
                    || element.SerializedOwningElementId != parent.Id
                    || element.SerializedOwnElementClassId != child.ClassId)) continue;
            var handles = new List<int>();
            foreach (var child in core)
            {
                string expectedClass = child.ClassId switch
                {
                    3708 => "IFCSTAIRFLIGHT", 3686 => "IFCSLAB", _ => "IFCMEMBER",
                };
                if (!products.TryGetValue(child.Id, out var product) || product.ifcType != expectedClass
                    || !indexById.TryGetValue(product.entityId, out int index)
                    || decomposed.Contains(product.entityId)) continue;
                var entity = writer.EntityAt(index);
                var args = IfcExporter.SplitStepArguments(entity.Args);
                if (entity.Type == expectedClass && args.Length >= 8
                    && args[7] == StepWriter.Str(child.Id.ToString()) && HasBody(args[6]))
                    handles.Add(entity.Id);
            }
            if (handles.Count == 0) continue;
            string? validatedParentName = IfcExporter.ResolveNativeStairName(parent, unique, counts, currentProfile);
            if (validatedParentName != null && parentArgs[2] == StepWriter.Str(validatedParentName))
            {
                // The index is incremented before the Body success gate, separately for
                // runs, landings and supports; the numeric order of the support set is fixed.
                // Never renumber around a filtered/failed sibling.
                foreach (ushort role in new ushort[] { 3708, 3686, 3727 })
                {
                    int ordinal = 0;
                    foreach (var child in core.Where(child => child.ClassId == role).OrderBy(child => child.Id))
                    {
                        ordinal++;
                        if (!products.TryGetValue(child.Id, out var product) || !handles.Contains(product.entityId)) continue;
                        string original = validatedParentName + (role switch
                            { 3708 => " Run ", 3686 => " Landing ", _ => " Stringer " })
                            + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        string? name = IfcExporter.ResolveNativeStairNameOverride(unique[child.Id], original);
                        if (name == null) continue;
                        int childIndex = indexById[product.entityId];
                        var args = IfcExporter.SplitStepArguments(writer.EntityAt(childIndex).Args);
                        args[2] = StepWriter.Str(name);
                        writer.ReplaceArgs(childIndex, string.Join(",", args));
                    }
                }
            }
            // Railings use Railing.HostId, not the stair's incomplete registered
            // list or m_OwningElementId. The container must already have
            // admitted a successful core component before railings can join it.
            foreach (var railing in railingsByHost[parent.Id])
            {
                if (railing.Id <= 0 || railing.Id == parent.Id || railing.RuleClassName != "BaseRailing"
                    || railing.SerializedOwningElementId == null
                    || !products.TryGetValue(railing.Id, out var product) || product.ifcType != "IFCRAILING"
                    || decomposed.Contains(product.entityId)
                    || !indexById.TryGetValue(product.entityId, out int index)) continue;
                var entity = writer.EntityAt(index);
                var args = IfcExporter.SplitStepArguments(entity.Args);
                if (entity.Type == "IFCRAILING" && args.Length == 9
                    && args[7] == StepWriter.Str(railing.Id.ToString()) && HasBody(args[6])) handles.Add(entity.Id);
            }
            // The relation GlobalId hashes "IfcRelAggregates:" + the actual parent GlobalId; this is not an index XOR.
            string guid = IfcGuid.FromMd5("IfcRelAggregates:" + parentArgs[0][1..^1]);
            writer.Add("IFCRELAGGREGATES", $"{StepWriter.Str(guid)},{StepWriter.Ref(ownerHistory)},$,$,"
                + $"{StepWriter.Ref(emittedParent.entityId)},{StepWriter.List(handles.Order().Select(StepWriter.Ref))}");
            parentArgs[6] = "$";
            writer.ReplaceArgs(parentIndex, string.Join(",", parentArgs));
            var handleSet = handles.ToHashSet();
            foreach (var contents in storeyContents.Values) contents.RemoveAll(handleSet.Contains);
            decomposed.UnionWith(handles);
            existingParents.Add(emittedParent.entityId);
        }

        bool HasBody(string shape)
        {
            if (!Id(shape, out int id) || !indexById.TryGetValue(id, out int index)) return false;
            var entity = writer.EntityAt(index);
            var args = IfcExporter.SplitStepArguments(entity.Args);
            if (entity.Type != "IFCPRODUCTDEFINITIONSHAPE" || args.Length != 3) return false;
            foreach (string representation in List(args[2]))
            {
                if (!Id(representation, out int repId) || !indexById.TryGetValue(repId, out int repIndex)) continue;
                var rep = writer.EntityAt(repIndex);
                var repArgs = IfcExporter.SplitStepArguments(rep.Args);
                if (rep.Type == "IFCSHAPEREPRESENTATION" && repArgs.Length == 4 && repArgs[1] == "'Body'"
                    && List(repArgs[3]) is { Length: > 0 } items
                    && items.All(item => Id(item, out int itemId) && indexById.ContainsKey(itemId))) return true;
            }
            return false;
        }
    }

    /// <summary>Sub-element of the ExportId that names the stair holding the body (the GlobalId of
    /// the second IfcStair differs from the first by 2 in bits 32-63).</summary>
    internal const int LegacyStairBodySubElementIndex = 2;

    /// <summary>A stair of the legacy class Stairs (it has no runs, landings or supports) is exported by the current profile as an
    /// IfcStair without representation that aggregates a second IfcStair under a copy of its placement; the second holds the
    /// body, and the railings hosted by the stair are aggregated next to it (a stair with runs is one IfcStair that aggregates them).
    /// The property sets, the spatial containment and the name stay with the first, the type is shared. Returns, for each stair
    /// split, the entity id of the second IfcStair.</summary>
    internal static Dictionary<int, int> SplitLegacyBodies(StepWriter writer, IReadOnlyList<Element> elements,
        IReadOnlyDictionary<int, (int entityId, int placementId, string ifcType)> products,
        Dictionary<int, List<int>> storeyContents, int ownerHistory, int axisId, ISet<Guid> usedExportIds)
    {
        var bodies = new Dictionary<int, int>();
        var stairs = elements.Where(element => element.Id > 0 && !element.IsBlockOnly && element.CategoryId == -2000120
            && element.RootClassName == "Stairs" && products.TryGetValue(element.Id, out var product) && product.ifcType == "IFCSTAIR")
            .OrderBy(element => element.Id).ToList();
        if (stairs.Count == 0) return bodies;
        var counts = new Dictionary<int, int>();
        foreach (var element in elements) counts[element.Id] = counts.GetValueOrDefault(element.Id) + 1;
        var railingsByHost = elements.Where(element => element.Id > 0 && !element.IsBlockOnly && counts[element.Id] == 1
                && element.SerializedRailingStairHostId is > 0 && element.RuleClassName == "BaseRailing"
                && element.SerializedOwningElementId != null)
            .ToLookup(element => element.SerializedRailingStairHostId!.Value);
        var indexById = new Dictionary<int, int>(writer.EntityCount);
        var decomposed = new HashSet<int>();
        for (int index = 0; index < writer.EntityCount; index++)
        {
            var entity = writer.EntityAt(index);
            indexById.Add(entity.Id, index);
            if (entity.Type is not ("IFCRELAGGREGATES" or "IFCRELNESTS")) continue;
            var args = IfcExporter.SplitStepArguments(entity.Args);
            if (args.Length == 6) foreach (string child in List(args[5])) if (Id(child, out int id)) decomposed.Add(id);
        }
        foreach (var stair in stairs)
        {
            if (counts[stair.Id] != 1) continue;
            var parent = products[stair.Id];
            int parentIndex = indexById[parent.entityId];
            var parentArgs = IfcExporter.SplitStepArguments(writer.EntityAt(parentIndex).Args);
            if (writer.EntityAt(parentIndex).Type != "IFCSTAIR" || parentArgs.Length != 9
                || parentArgs[7] != StepWriter.Str(stair.Id.ToString()) || parentArgs[6] == "$"
                || parentArgs[0].Length != 24 || decomposed.Contains(parent.entityId)) continue;
            string parentGuid = parentArgs[0][1..^1];
            string bodyGuid = IfcExporter.ResolveExactElementGlobalId(stair, usedExportIds, LegacyStairBodySubElementIndex, true)
                ?? IfcGuid.FromName($"stair-body:{stair.Id}");
            int placement = writer.Add("IFCLOCALPLACEMENT", $"{parentArgs[5]},{StepWriter.Ref(axisId)}");
            int body = writer.Add("IFCSTAIR", string.Join(',', StepWriter.Str(bodyGuid), parentArgs[1], parentArgs[2], parentArgs[3],
                parentArgs[4], StepWriter.Ref(placement), parentArgs[6], parentArgs[7], parentArgs[8]));
            parentArgs[6] = "$";
            writer.ReplaceArgs(parentIndex, string.Join(",", parentArgs));
            var handles = new List<int> { body };
            foreach (var railing in railingsByHost[stair.Id].OrderBy(railing => railing.Id))
            {
                if (railing.Id == stair.Id || !products.TryGetValue(railing.Id, out var product) || product.ifcType != "IFCRAILING"
                    || decomposed.Contains(product.entityId)) continue;
                handles.Add(product.entityId);
            }
            writer.Add("IFCRELAGGREGATES", $"{StepWriter.Str(IfcGuid.FromMd5("IfcRelAggregates:" + parentGuid))},{StepWriter.Ref(ownerHistory)},$,$,"
                + $"{StepWriter.Ref(parent.entityId)},{StepWriter.List(handles.Select(StepWriter.Ref))}");
            var handleSet = handles.ToHashSet();
            foreach (var contents in storeyContents.Values) contents.RemoveAll(handleSet.Contains);
            decomposed.UnionWith(handles);
            bodies[stair.Id] = body;
        }
        return bodies;
    }

    private static bool Id(string value, out int id)
    {
        id = 0;
        return value.StartsWith('#') && int.TryParse(value.AsSpan(1), out id) && id > 0;
    }
    private static string[] List(string value) => value.StartsWith('(') && value.EndsWith(')') && value.Length > 2
        ? IfcExporter.SplitStepArguments(value[1..^1]) : [];
}
