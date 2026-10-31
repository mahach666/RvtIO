using System.Globalization;

namespace RvtIO.Ifc;

public partial class IfcExporter
{
    /// <summary>Phases of the creation order of the openings of one host (<see cref="ApplyCurrentOpeningOrder"/>).</summary>
    internal const int OpeningOrderCutter = 0, OpeningOrderOwnLoop = 1, OpeningOrderDelayed = 2;

    /// <summary>Puts the IfcOpeningElements of every host in the order the current profile creates them: first the openings of
    /// the host's wall openings and of its cutters, in the order of <c>openingIndex</c> (the number in their GlobalId,
    /// <see cref="CutOpeningGlobalId"/>); then the openings of the host's own sketch loops (<c>IfcOpeningElement:{n}:</c>, n ascending);
    /// last the delayed openings of the doors and windows hosted by the wall, by the id of the door or window.
    /// The writers of this exporter create them in another order (the openings of a door or window with the door, the pair
    /// openings after all products), so each host's opening entities are permuted among the places they already occupy.
    /// An opening without a known place (its GlobalId was not derived) goes after the others. Returns the hosts changed.</summary>
    internal static int ApplyCurrentOpeningOrder(StepWriter w, IReadOnlyDictionary<int, (int Group, int Major, int Minor)> keys)
    {
        var hostOfOpening = new Dictionary<int, int>();
        var filled = new HashSet<int>();
        for (int i = 0; i < w.EntityCount; i++)
        {
            var (_, type, args) = w.EntityAt(i);
            if (type is not ("IFCRELVOIDSELEMENT" or "IFCRELFILLSELEMENT")) continue;
            var references = EntityReference.Matches(args);
            if (references.Count < 3) continue;
            int relating = int.Parse(references[^2].Groups[1].ValueSpan, CultureInfo.InvariantCulture);
            int related = int.Parse(references[^1].Groups[1].ValueSpan, CultureInfo.InvariantCulture);
            if (type == "IFCRELVOIDSELEMENT") hostOfOpening[related] = relating;
            else filled.Add(relating);
        }

        var positionsByHost = new Dictionary<int, List<int>>();
        for (int i = 0; i < w.EntityCount; i++)
        {
            var (id, type, _) = w.EntityAt(i);
            if (type != "IFCOPENINGELEMENT" || !hostOfOpening.TryGetValue(id, out int host)) continue;
            if (!positionsByHost.TryGetValue(host, out var positions)) positionsByHost[host] = positions = [];
            positions.Add(i);
        }

        int changed = 0;
        foreach (var positions in positionsByHost.Values)
        {
            if (positions.Count < 2) continue;
            var order = positions.OrderBy(position =>
            {
                int id = w.EntityAt(position).Id;
                if (keys.TryGetValue(id, out var key)) return (0, key.Group, key.Major, key.Minor);
                return filled.Contains(id) ? (1, OpeningOrderDelayed, int.MaxValue, 0) : (2, 0, 0, 0);
            }).ThenBy(position => position).ToList();
            if (order.SequenceEqual(positions)) continue;
            w.PlaceEntities(positions, order);
            changed++;
        }
        return changed;
    }
}
