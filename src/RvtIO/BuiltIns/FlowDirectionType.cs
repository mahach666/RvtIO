namespace RvtIO.BuiltIns;

/// <summary>Flow directions of a connector.</summary>
public enum FlowDirectionType
{
    /// <summary>The flow can go either way.</summary>
    Bidirectional = 0,
    /// <summary>The flow enters the connector.</summary>
    In = 1,
    /// <summary>The flow leaves the connector.</summary>
    Out = 2,
}
