namespace RvtIO.BuiltIns;

/// <summary>Flow configurations of a connector.</summary>
public enum PipeFlowConfigurationType
{
    /// <summary>The flow is calculated from the connected elements.</summary>
    Calculated = 0,
    /// <summary>The flow is a preset value.</summary>
    Preset = 1,
    /// <summary>The flow comes from the system.</summary>
    System = 2,
    /// <summary>The flow follows the demand of the fixtures.</summary>
    Demand = 3,
}
