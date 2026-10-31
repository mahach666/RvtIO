namespace RvtIO.Compact;

/// <summary>Options of the compact JSON export.</summary>
public sealed class CompactJsonExportOptions
{
    private readonly string _jsonClassNamePrefix = "";

    /// <summary>
    /// Text put in front of every class name written to the "name" field of a node (for example "Cls" turns "Floor" into
    /// "ClsFloor"). Empty by default.
    /// </summary>
    public string JsonClassNamePrefix
    {
        get => _jsonClassNamePrefix;
        init => _jsonClassNamePrefix = value ?? throw new ArgumentNullException(nameof(value));
    }
}
