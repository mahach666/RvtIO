namespace RvtIO.Parsing;

/// <summary>The outline of a room or an area that has no geometry of its own, from the circuit of the level room plan that holds it.
/// The current profile extrudes it (the space export, the branch for an element without geometry): the curves of
/// the boundary segments of the room make a curve loop, the extrusion export turns it into the
/// profile. A side of the circuit is a segment of the plan, walked backwards when its sub index is negative (the segment -sub - 1);
/// the vertices of the profile are the points of the curves in order, each curve after the first contributing all but its first point
///, the closing point dropped.</summary>
internal static class RoomPlanBoundary
{
    /// <summary>Two ends closer than this are one point: the plan stores the ends of neighbouring segments to 1e-13, the smallest real gap is 1e-2 feet.</summary>
    internal const double SameVertex = 1e-9;

    /// <summary>The longest step of the sample points of an arc (equal steps of the arc angle with a zero tolerance): pi/16.</summary>
    internal const double ArcStep = 0.1963495408493621;

    /// <summary>The vertices (feet, in the coordinates of the plan) of the outline of the circuit, null when it cannot be told exactly:
    /// a side without segment, a segment of more than two points that is not an arc, an island in the circuit, a gap between curves that
    /// the loop cannot close the way the examples show (the neighbours of an arc, parallel curves on one line).</summary>
    internal static List<(double X, double Y)>? OuterLoop(TfBlockDeserializer.RoomPlanTopology plan, int circuit) => OuterLoop(plan, circuit, out _);

    /// <inheritdoc cref="OuterLoop(TfBlockDeserializer.RoomPlanTopology, int)"/>
    /// <param name="failure">Why there is no loop (null when there is one); RVTIO_PLAN_LOG=1 prints it.</param>
    internal static List<(double X, double Y)>? OuterLoop(TfBlockDeserializer.RoomPlanTopology plan, int circuit, out string? failure)
    {
        failure = null;
        if (plan.Segments is not { } segments || plan.CircuitSides is not { } circuits || !circuits.TryGetValue(circuit, out var sides) || sides.Count == 0)
            return Fail(out failure, "the plan holds no sides of the circuit");
        if (plan.Components.Any(component => component.Enclosing == circuit)) return Fail(out failure, "the circuit encloses an island");
        var byKey = new Dictionary<TfBlockDeserializer.SegmentKey, TfBlockDeserializer.PlanSegment>();
        foreach (var segment in segments) byKey[segment.Key] = segment;

        // A segment walked out and straight back (a dangling boundary line) is no part of the outline.
        var walk = new List<TfBlockDeserializer.SegmentKey>();
        foreach (var side in sides)
        {
            if (walk.Count > 0 && walk[^1] is var last && last.Item == side.Item && last.LinkRef == side.LinkRef && last.Major == side.Major
                && (last.Sub >= 0 ? side.Sub == -last.Sub - 1 : last.Sub == -side.Sub - 1))
                walk.RemoveAt(walk.Count - 1);
            else
                walk.Add(side);
        }

        var curves = new List<Curve>();
        foreach (var side in walk)
        {
            bool backwards = side.Sub < 0;
            var key = backwards ? side with { Sub = -side.Sub - 1 } : side;
            if (!byKey.TryGetValue(key, out var segment) || segment.Points.Count < 2) return Fail(out failure, $"no segment for the side {side}");
            var points = backwards ? segment.Points.AsEnumerable().Reverse().ToList() : segment.Points.ToList();
            string notArc = "";
            // a segment whose points lie in order on one line is the straight line between its ends
            var curve = points.Count == 2 || IsStraight(points) ? new Curve([points[0], points[^1]], false) : SampleArc(points, out notArc);
            if (curve == null) return Fail(out failure, $"side {side.Item}/{side.LinkRef}/{side.Major}/{side.Sub}: {points.Count} points, {notArc}"
                + string.Concat(points.Select((point, index) => $"{Environment.NewLine}  p{index} {point.X:R} {point.Y:R}")));
            curves.Add(curve);
        }

        // Building the curve loop: where one curve does not end at the start of the next, two straight lines that cross are
        // cut at the crossing, otherwise a straight line joins them. The one that closes the loop comes first in it.
        Curve? leading = null;
        var loop = new List<Curve>();
        for (int index = 0; index < curves.Count; index++)
        {
            var current = curves[index];
            var next = curves[(index + 1) % curves.Count];
            loop.Add(current);
            if (Same(current.End, next.Start)) continue;
            if (current.Arc || next.Arc) return Fail(out failure, $"a gap of {Math.Sqrt(Math.Pow(current.End.X - next.Start.X, 2) + Math.Pow(current.End.Y - next.Start.Y, 2)):G4} feet beside an arc");
            var crossing = Cross(current, next);
            if (crossing is { } corner)
            {
                current.Points[^1] = corner;
                next.Points[0] = corner;
                continue;
            }
            if (IsCollinear(current, next)) return Fail(out failure, "a gap along one line");
            var connector = new Curve([current.End, next.Start], false);
            if (index == curves.Count - 1) leading = connector;
            else loop.Add(connector);
        }
        if (leading != null) loop.Insert(0, leading);

        var outline = new List<(double X, double Y)>();
        for (int index = 0; index < loop.Count; index++)
            outline.AddRange(index == 0 ? loop[index].Points : loop[index].Points.Skip(1));
        if (outline.Count < 4 || !Same(outline[0], outline[^1])) return Fail(out failure, "the curves do not close");
        outline.RemoveAt(outline.Count - 1);
        return outline.Count >= 3 ? outline : Fail(out failure, "fewer than three vertices");
    }

    private static List<(double X, double Y)>? Fail(out string? failure, string reason)
    {
        failure = reason;
        return null;
    }

    private sealed class Curve((double X, double Y)[] points, bool arc)
    {
        public (double X, double Y)[] Points { get; } = points;
        public bool Arc { get; } = arc;
        public (double X, double Y) Start => Points[0];
        public (double X, double Y) End => Points[^1];
    }

    /// <summary>The sample points the expected export takes of the arc the plan polyline lies on: equal steps of at most pi/16 from the first to the last point,
    /// null when the points are not on one circle.</summary>
    private static Curve? SampleArc(List<(double X, double Y)> points, out string notArc)
    {
        notArc = "points on a line";
        var (first, middle, last) = (points[0], points[points.Count / 2], points[^1]);
        double d = 2 * (first.X * (middle.Y - last.Y) + middle.X * (last.Y - first.Y) + last.X * (first.Y - middle.Y));
        if (Math.Abs(d) < 1e-12) return null;
        double f = first.X * first.X + first.Y * first.Y, m = middle.X * middle.X + middle.Y * middle.Y, l = last.X * last.X + last.Y * last.Y;
        var centre = ((f * (middle.Y - last.Y) + m * (last.Y - first.Y) + l * (first.Y - middle.Y)) / d,
            (f * (last.X - middle.X) + m * (first.X - last.X) + l * (middle.X - first.X)) / d);
        double radius = Math.Sqrt(Math.Pow(first.X - centre.Item1, 2) + Math.Pow(first.Y - centre.Item2, 2));
        double sweep = 0, firstStep = 0;
        for (int i = 0; i < points.Count; i++)
        {
            double spread = Math.Abs(Math.Sqrt(Math.Pow(points[i].X - centre.Item1, 2) + Math.Pow(points[i].Y - centre.Item2, 2)) - radius);
            if (spread > 1e-9 * Math.Max(1, radius))
            {
                notArc = $"point {i} is {spread:G3} off the circle of r {radius:G4}";
                return null;
            }
            if (i == 0) continue;
            double a = Math.Atan2(points[i - 1].Y - centre.Item2, points[i - 1].X - centre.Item1);
            double b = Math.Atan2(points[i].Y - centre.Item2, points[i].X - centre.Item1);
            double step = b - a;
            if (step > Math.PI) step -= 2 * Math.PI;
            else if (step < -Math.PI) step += 2 * Math.PI;
            // the plan stores an arc in equal steps of its angle
            if (i == 1) firstStep = step;
            else if (Math.Abs(step - firstStep) > 1e-6 * Math.Abs(firstStep))
            {
                notArc = $"unequal steps {firstStep:G6}, {step:G6}";
                return null;
            }
            sweep += step;
        }
        // a circular arc covers at most one turn (at most 32 steps): the stored points are untrusted, a polyline winding round the circle many
        // times must not size the sample array
        if (Math.Abs(sweep) > 2 * Math.PI + 1e-6)
        {
            notArc = $"a sweep of {Math.Abs(sweep):G6} rad is more than a full turn";
            return null;
        }
        int count = (int)Math.Ceiling(Math.Abs(sweep) / ArcStep);
        if (count < 1)
        {
            notArc = "no sweep";
            return null;
        }
        double start = Math.Atan2(first.Y - centre.Item2, first.X - centre.Item1);
        var samples = new (double X, double Y)[count + 1];
        for (int k = 0; k <= count; k++)
        {
            double angle = start + sweep * k / count;
            samples[k] = (centre.Item1 + radius * Math.Cos(angle), centre.Item2 + radius * Math.Sin(angle));
        }
        samples[0] = first;
        samples[count] = last;
        return new Curve(samples, true);
    }

    /// <summary>The points lie on the line from the first to the last, in order.</summary>
    private static bool IsStraight(List<(double X, double Y)> points)
    {
        var (first, last) = (points[0], points[^1]);
        double dx = last.X - first.X, dy = last.Y - first.Y, length = Math.Sqrt(dx * dx + dy * dy);
        if (!(length > SameVertex)) return false;
        double previous = 0;
        foreach (var point in points)
        {
            double along = ((point.X - first.X) * dx + (point.Y - first.Y) * dy) / length;
            if (Math.Abs((point.X - first.X) * dy - (point.Y - first.Y) * dx) / length > SameVertex || along < previous - SameVertex) return false;
            previous = along;
        }
        return true;
    }

    private static bool Same((double X, double Y) a, (double X, double Y) b) => Math.Abs(a.X - b.X) <= SameVertex && Math.Abs(a.Y - b.Y) <= SameVertex;

    private static bool IsParallel(Curve a, Curve b)
    {
        double ax = a.End.X - a.Start.X, ay = a.End.Y - a.Start.Y, bx = b.End.X - b.Start.X, by = b.End.Y - b.Start.Y;
        return Math.Abs(ax * by - ay * bx) < 1e-9 * Math.Sqrt(ax * ax + ay * ay) * Math.Sqrt(bx * bx + by * by);
    }

    private static bool IsCollinear(Curve a, Curve b)
    {
        if (!IsParallel(a, b)) return false;
        double ax = a.End.X - a.Start.X, ay = a.End.Y - a.Start.Y;
        double off = Math.Abs(ax * (b.Start.Y - a.Start.Y) - ay * (b.Start.X - a.Start.X)) / Math.Sqrt(ax * ax + ay * ay);
        return off <= SameVertex;
    }

    /// <summary>Where the infinite lines of two straight curves cross; null when they are parallel.</summary>
    private static (double X, double Y)? Cross(Curve a, Curve b)
    {
        if (IsParallel(a, b)) return null;
        double dx1 = a.End.X - a.Start.X, dy1 = a.End.Y - a.Start.Y, dx2 = b.End.X - b.Start.X, dy2 = b.End.Y - b.Start.Y;
        double t = ((b.Start.X - a.Start.X) * dy2 - (b.Start.Y - a.Start.Y) * dx2) / (dx1 * dy2 - dy1 * dx2);
        return (a.Start.X + t * dx1, a.Start.Y + t * dy1);
    }
}
