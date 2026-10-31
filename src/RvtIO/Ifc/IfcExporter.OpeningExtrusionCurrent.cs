using RvtIO.Model;

namespace RvtIO.Ifc;

public partial class IfcExporter
{
    internal bool TryCreateRectangularOpeningBodyCurrent(
        StepWriter w, GeometryData geometry, LocationCurve? wall, int subContextId,
        out int shapeId, out BodyRepresentationWriteOutcome outcome)
    {
        shapeId = 0;
        outcome = default;
        if (!CurrentProfile || !TryGetExactVerticalRectangularPrismSweptOpening(geometry, out var opening)
            || !RectangularOpeningHasWallExtrusionAxisCurrent(opening, wall)) return false;
        double dx = wall!.EndPoint[0] - wall.StartPoint[0], dy = wall.EndPoint[1] - wall.StartPoint[1];
        double length = Math.Sqrt(dx * dx + dy * dy);
        // The opening is extruded along Z cross the host's actual axis.
        return TryCreateOthersCurrentProductSweptSolid(w, geometry, subContextId,
            [[-dy / length, dx / length, 0]], string.Empty, out shapeId, out outcome);
    }

    internal static bool RectangularOpeningHasWallExtrusionAxisCurrent(HostedOpeningGeometry opening, LocationCurve? wall)
    {
        if (wall is not { CurveType: "Line", ArcRadius: null }
            || wall.StartPoint.Length < 3 || wall.EndPoint.Length < 3) return false;
        double dx = wall.EndPoint[0] - wall.StartPoint[0], dy = wall.EndPoint[1] - wall.StartPoint[1];
        double wallLength = Math.Sqrt(dx * dx + dy * dy);
        double axisLength = Math.Sqrt(opening.AxisX * opening.AxisX + opening.AxisY * opening.AxisY);
        if (!(wallLength > 0) || !(axisLength > 0) || !double.IsFinite(wallLength) || !double.IsFinite(axisLength)) return false;
        double x = dx / wallLength, y = dy / wallLength;
        double ax = opening.AxisX / axisLength, ay = opening.AxisY / axisLength;
        // The wall's custom normal is used; the vector equality tolerance is 1e-10.
        return Parallel(x, y, ax, ay) || Parallel(x, y, -ay, ax);

        static bool Parallel(double x, double y, double ax, double ay) =>
            Math.Sqrt((x - ax) * (x - ax) + (y - ay) * (y - ay)) < 1e-10
            || Math.Sqrt((x + ax) * (x + ax) + (y + ay) * (y + ay)) < 1e-10;
    }
}
