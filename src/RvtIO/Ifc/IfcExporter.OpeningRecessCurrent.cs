using RvtIO.Model;

namespace RvtIO.Ifc;

public partial class IfcExporter
{
    /// <summary>The openings the current profile gives the ObjectType "Recess": the length of the extrusion made for the opening
    /// solid (the Depth quantity of the opening) is compared with the total layer width of the host; shorter is a "Recess", else
    /// "Opening". A solid written as an IfcFacetedBrep has no extrusion, a length of 0, and is an "Opening". Always an "Opening":
    /// the openings of the host's own sketch loops and the extrusion a door or window makes of its own cut loops (named after
    /// the filling instance). An opening whose depth or whose host's width cannot be read keeps the shallower-than-host test of
    /// its writer (<paramref name="writerRecess"/>), unless it is a Brep.</summary>
    internal HashSet<int> OpeningRecessEntitiesCurrent(
        StepWriter w, IReadOnlyDictionary<int, Element> elementsById, IReadOnlyDictionary<int, int> productElementIdByEntity,
        IReadOnlyDictionary<int, (int entityId, int placementId, string ifcType)> products,
        IReadOnlyDictionary<int, (int Group, int Major, int Minor)> orderKeys, IReadOnlySet<int> writerRecess)
    {
        var indexById = new Dictionary<int, int>(w.EntityCount);
        var context = new QuantityContextCurrent
        {
            W = w, IndexById = indexById, Elements = elementsById, ElementIdByEntity = productElementIdByEntity, Products = products,
        };
        var openings = new List<int>();
        for (int i = 0; i < w.EntityCount; i++)
        {
            var (id, type, args) = w.EntityAt(i);
            indexById[id] = i;
            if (type == "IFCOPENINGELEMENT") openings.Add(i);
            else if (type is "IFCRELVOIDSELEMENT" or "IFCRELFILLSELEMENT")
            {
                var relation = SplitStepArguments(args);
                if (relation.Length != 6 || !TryEntityId(relation[4], out int relating) || !TryEntityId(relation[5], out int related))
                    continue;
                if (type == "IFCRELVOIDSELEMENT") context.HostByOpening[related] = relating;
                else context.FilledOpenings.Add(relating);
            }
        }

        const double tolerance = RecessToleranceFt * FeetToMetres;
        var recess = new HashSet<int>();
        foreach (int index in openings)
        {
            var (id, type, args) = w.EntityAt(index);
            var arguments = SplitStepArguments(args);
            if (arguments.Length != 8 || !context.HostByOpening.TryGetValue(id, out int host)
                || orderKeys.TryGetValue(id, out var key) && key.Group == OpeningOrderOwnLoop)
                continue;
            // A filled opening that is not named after its host is the extrusion of the door or window itself.
            if (context.FilledOpenings.Contains(id) && indexById.TryGetValue(host, out int hostIndex)
                && SplitStepArguments(w.EntityAt(hostIndex).Args) is { Length: > 2 } hostArguments && hostArguments[2] != arguments[2])
                continue;

            var depth = QuantitiesOfProductCurrent(context, id, type, args).Where(quantity => quantity.Name == "Depth").ToList();
            if (depth.Count > 0 && WallWidthCurrent(context, host) is { } width)
            {
                if (width > 1e-10 && depth[0].Value < width - tolerance) recess.Add(id);
            }
            else if (writerRecess.Contains(id) && !OpeningBodyIsFacetedBrepCurrent(w, indexById, arguments[6]))
                recess.Add(id);
        }
        return recess;
    }

    /// <summary>Whether the single Body item of the shape is an IfcFacetedBrep.</summary>
    private static bool OpeningBodyIsFacetedBrepCurrent(StepWriter w, Dictionary<int, int> indexById, string shapeArgument)
    {
        if (!TryEntityId(shapeArgument, out int shapeId) || !indexById.TryGetValue(shapeId, out int shapeIndex)) return false;
        var shape = SplitStepArguments(w.EntityAt(shapeIndex).Args);
        if (shape.Length != 3 || shape[2].Length < 3) return false;
        foreach (string representationToken in SplitStepArguments(shape[2][1..^1]))
        {
            if (!TryEntityId(representationToken, out int representationId) || !indexById.TryGetValue(representationId, out int representationIndex))
                continue;
            var representation = SplitStepArguments(w.EntityAt(representationIndex).Args);
            if (representation.Length != 4 || representation[1] != "'Body'" || representation[3].Length < 3) continue;
            return SplitStepArguments(representation[3][1..^1]).Any(item =>
                TryEntityId(item, out int itemId) && indexById.TryGetValue(itemId, out int itemIndex)
                && w.EntityAt(itemIndex).Type == "IFCFACETEDBREP");
        }
        return false;
    }
}
