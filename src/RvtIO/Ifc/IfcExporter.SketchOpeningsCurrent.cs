using System.Globalization;
using RvtIO.Model;

namespace RvtIO.Ifc;

public partial class IfcExporter
{
    /// <summary>One hole of a wall's profile sketch: its index among the sketch loops, its extent along the wall (u) and
    /// vertically (v), the position of its plane across the wall (feet) and whether it is a rectangle.</summary>
    internal readonly record struct SketchHoleCurrent(
        int LoopIndex, double U0, double U1, double V0, double V1, double Across, bool Rectangular);

    /// <summary>The holes of the closed loops of one profile sketch of a wall (the
    /// loops that are not an outer boundary): a loop inside an odd
    /// number of the other loops. The loops are taken in the wall's elevation, (u, v) = (along the wall axis, z); a sketch
    /// whose loops do not lie in one plane parallel to the wall has none.</summary>
    internal static List<SketchHoleCurrent> SketchHolesCurrent(IReadOnlyList<IReadOnlyList<double[]>> loops, double axisX, double axisY)
    {
        var polygons = new List<List<(double U, double V)>>();
        var across = new List<double>();
        foreach (var loop in loops)
        {
            var points = loop.Where(p => p.Length >= 3 && p.Take(3).All(double.IsFinite)).ToList();
            // a loop closed by repeating its first point (the walk of BuildSketchLoops ends on the start vertex, which can
            // differ from the first point in the last digits)
            if (points.Count > 1 && Enumerable.Range(0, 3).All(axis => Math.Abs(points[0][axis] - points[^1][axis]) < ClosureToleranceFt))
                points.RemoveAt(points.Count - 1);
            if (points.Count < 3) return [];
            polygons.Add(points.Select(p => (p[0] * axisX + p[1] * axisY, p[2])).ToList());
            across.AddRange(points.Select(p => -p[0] * axisY + p[1] * axisX));
        }
        if (polygons.Count < 2 || across.Max() - across.Min() > SketchPlaneToleranceFt) return [];

        static bool Inside(List<(double U, double V)> polygon, (double U, double V) point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var (ui, vi) = polygon[i]; var (uj, vj) = polygon[j];
                if (vi > point.V != vj > point.V && point.U < (uj - ui) * (point.V - vi) / (vj - vi) + ui) inside = !inside;
            }
            return inside;
        }

        static bool IsRectangle(List<(double U, double V)> polygon, out double u0, out double u1, out double v0, out double v1)
        {
            u0 = polygon.Min(p => p.U); u1 = polygon.Max(p => p.U); v0 = polygon.Min(p => p.V); v1 = polygon.Max(p => p.V);
            double u0Copy = u0, u1Copy = u1, v0Copy = v0, v1Copy = v1;
            return polygon.Count == 4 && polygon.All(p =>
                (Math.Abs(p.U - u0Copy) < SketchPlaneToleranceFt || Math.Abs(p.U - u1Copy) < SketchPlaneToleranceFt)
                && (Math.Abs(p.V - v0Copy) < SketchPlaneToleranceFt || Math.Abs(p.V - v1Copy) < SketchPlaneToleranceFt));
        }

        var holes = new List<SketchHoleCurrent>();
        int outerLoops = 0;
        bool outerIsRectangle = true;
        for (int index = 0; index < polygons.Count; index++)
        {
            // shortcut: the first vertex decides, which holds for loops that do not touch each other; upgrade when a sketch
            // has a hole touching another loop's outline.
            int containing = 0;
            for (int other = 0; other < polygons.Count; other++)
                if (other != index && Inside(polygons[other], polygons[index][0])) containing++;
            bool rectangular = IsRectangle(polygons[index], out double u0, out double u1, out double v0, out double v1);
            if (containing % 2 == 0)
            {
                outerLoops++;
                outerIsRectangle &= rectangular;
                continue;
            }
            holes.Add(new SketchHoleCurrent(index, u0, u1, v0, v1, across.Average(), rectangular));
        }
        // Only a sketch with one rectangular outline has holes here; walls whose outline has 12 or
        // 16 lines are IfcWall (no opening of any writer).
        return outerLoops == 1 && outerIsRectangle ? holes : [];
    }

    /// <summary>Distance below which two sketch points lie in one plane or on one line (about 0.03 mm).</summary>
    private const double SketchPlaneToleranceFt = 1e-4;

    /// <summary>Distance below which the last point of a loop is its first (the walk ends on the start vertex).</summary>
    private const double ClosureToleranceFt = 1e-6;

    /// <summary>The current profile cuts an opening from the extrusion of a wall for every hole of its profile sketch
    /// (the non-outer loops extruded over the wall's total width and centred on the sketch plane).
    /// They are IfcOpeningElements of the wall like the pair openings and are written for the same walls
    /// (<see cref="IsAxisExtrusionHostCurrent"/>). The wall owns the sketch when the sketch owner references it once and every line of the
    /// hole references it once. The opening is tagged with a line of its loop;
    /// <see cref="ApplyCutOpeningIdentities"/> numbers them from the last loop. Other writers already write
    /// some of them; a hole one of them has tagged is not repeated.</summary>
    private void WriteSketchOpeningsCurrent(
        StepWriter w,
        IReadOnlyDictionary<int, (int entityId, int placementId, int storeyId)> wallIfcIds,
        IReadOnlyDictionary<int, (int entityId, int placementId, string ifcType)> exportedIfcElements,
        IReadOnlyDictionary<int, Element> elemById,
        Dictionary<int, List<int>> storeyContents,
        int parentPlacementId, int ownerHistId, int subCtxId,
        Dictionary<(string PropertyName, string ValueExpr), int> propertySingleValueIds)
    {
        var ownersByWall = new SortedDictionary<int, List<Element>>();
        foreach (var owner in elemById.Values.Where(element => element.SketchLoops is { Count: >= 2 }).OrderBy(element => element.Id))
            foreach (int wallId in owner.References?.Where(id => id != owner.Id && wallIfcIds.ContainsKey(id)).Distinct() ?? [])
            {
                if (owner.References!.Count(id => id == wallId) != 1) continue;
                if (!ownersByWall.TryGetValue(wallId, out var owners)) ownersByWall[wallId] = owners = [];
                owners.Add(owner);
            }
        if (ownersByWall.Count == 0) return;

        var linesByOwner = elemById.Values
            .Where(element => element.CategoryId == SketchLinesCategoryId && element.HostId.HasValue && element.Location != null)
            .GroupBy(element => element.HostId!.Value).ToDictionary(group => group.Key, group => group.OrderBy(e => e.Id).ToList());

        // The tags of the openings another writer has already produced, by host entity.
        var openingTags = new Dictionary<int, string>();
        var hostOfOpening = new Dictionary<int, int>();
        for (int i = 0; i < w.EntityCount; i++)
        {
            var (id, type, args) = w.EntityAt(i);
            if (type == "IFCOPENINGELEMENT")
            {
                if (SplitStepArguments(args) is { Length: 8 } attributes) openingTags[id] = attributes[7].Trim('\'');
            }
            else if (type == "IFCRELVOIDSELEMENT" && SplitStepArguments(args) is { Length: 6 } relation
                && TryEntityId(relation[4], out int relating) && TryEntityId(relation[5], out int related))
                hostOfOpening[related] = relating;
        }
        var tagsByHostEntity = openingTags.Where(pair => hostOfOpening.ContainsKey(pair.Key))
            .GroupBy(pair => hostOfOpening[pair.Key])
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Value).ToHashSet());

        int written = 0;
        foreach (var (hostId, owners) in ownersByWall)
        {
            if (WallWritesNoBodyOpeningsCurrent(exportedIfcElements, hostId)
                || !elemById.TryGetValue(hostId, out var host) || !IsAxisExtrusionHostCurrent(host)
                || !TryGetSerializedWallWidthFeet(host, out double widthFt)
                || host.Location is not { CurveType: "Line", ArcRadius: null } location)
                continue;
            double dx = location.EndPoint[0] - location.StartPoint[0], dy = location.EndPoint[1] - location.StartPoint[1];
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (!(length > 0) || !double.IsFinite(length)) continue;
            double axisX = dx / length, axisY = dy / length;
            var wallInfo = wallIfcIds[hostId];
            tagsByHostEntity.TryGetValue(wallInfo.entityId, out var hostTags);

            foreach (var owner in owners)
            {
                if (!linesByOwner.TryGetValue(owner.Id, out var lines)) continue;
                foreach (var hole in SketchHolesCurrent(owner.SketchLoops!, axisX, axisY))
                {
                    // shortcut: a hole that is not a rectangle is not written and
                    // does not count in the numbering; upgrade when one occurs.
                    if (!hole.Rectangular) continue;
                    var loop = owner.SketchLoops![hole.LoopIndex];
                    var loopLines = lines.Where(line => OnLoop(loop, line.Location!.StartPoint) && OnLoop(loop, line.Location.EndPoint)).ToList();
                    var keys = loopLines.Select(line => line.SketchLoopKey).Distinct().ToList();
                    if (loopLines.Count != 4 || keys.Count != 1 || keys[0] is not int key
                        || loopLines.Any(line => line.References?.Count(id => id == hostId) != 1)
                        || hostTags != null && loopLines.Any(line => hostTags.Contains(line.Id.ToString(CultureInfo.InvariantCulture))))
                        continue;
                    double middle = (hole.U0 + hole.U1) / 2;
                    var opening = new RectangularWallThroughOpening(
                        middle * axisX - hole.Across * axisY, middle * axisY + hole.Across * axisX, axisX, axisY,
                        hole.U1 - hole.U0, hole.V0, hole.V1 - hole.V0, hole.Across - widthFt / 2, hole.Across + widthFt / 2);
                    if (!storeyContents.TryGetValue(wallInfo.storeyId, out var wallStoreyList))
                        storeyContents[wallInfo.storeyId] = wallStoreyList = new List<int>();
                    // The same writer as the exact-id openings of a wall's profile: the current profile hands the wrapper no
                    // element for them, so they carry no property sets; the loop key stands for the index in the placeholder name.
                    int openingEntityId = CreateExactSourceWallOpening(w, host, wallInfo.entityId, parentPlacementId, ownerHistId,
                        subCtxId, wallStoreyList, opening, key, elemById, propertySingleValueIds, key);
                    if (!_cutPairOpeningsByHostCurrent.TryGetValue(hostId, out var hostOpenings))
                        _cutPairOpeningsByHostCurrent[hostId] = hostOpenings = [];
                    hostOpenings.Add(openingEntityId);
                    written++;
                }
            }
        }
        _diag?.WriteLine($"The current profile sketch openings: {written}");

        static bool OnLoop(IReadOnlyList<double[]> loop, double[] point) =>
            point.Length >= 3 && loop.Any(vertex => vertex.Length >= 3 && Math.Abs(vertex[0] - point[0]) < 1e-6
                && Math.Abs(vertex[1] - point[1]) < 1e-6 && Math.Abs(vertex[2] - point[2]) < 1e-6);
    }
}
