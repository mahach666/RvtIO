namespace RvtIO.Internal;

/// <summary>
/// Raw CurveDriver data extracted from 0xFFFF blocks (VWallDriver).
/// Coordinates are in LOCAL space - needs GRep Transform to convert to world.
/// </summary>
internal record CurveDriverData
{
    /// <summary>True if this is an arc curve, false for line.</summary>
    public bool IsArc { get; set; }

    /// <summary>Origin point [x, y, z] in local coordinates (feet).</summary>
    public double[] OriginPoint { get; set; } = new double[3];

    /// <summary>Direction vector [x, y, z] in local coordinates (unit vector).</summary>
    public double[] DirectionVector { get; set; } = new double[3];

    /// <summary>EndParams: parametric range along the curve [startParam, endParam].</summary>
    public double[] EndParams { get; set; } = new double[2];

    /// <summary>Arc X direction [x, y, z].</summary>
    public double[]? XDirection { get; set; }

    /// <summary>Arc Y direction [x, y, z].</summary>
    public double[]? YDirection { get; set; }

    /// <summary>Arc radius in feet.</summary>
    public double Radius { get; set; }

    /// <summary>Arc center point [x, y, z] in local coordinates.</summary>
    public double[]? CenterPoint { get; set; }
}
