namespace RvtIO.Model
{
    /// <summary>How a parameter value is stored.</summary>
    public enum StorageType
    {
        /// <summary>No value.</summary>
        None = 0,
        /// <summary>A 32-bit integer.</summary>
        Integer = 1,
        /// <summary>A floating point number in internal units.</summary>
        Double = 2,
        /// <summary>A text.</summary>
        String = 3,
        /// <summary>An element id.</summary>
        ElementId = 4,
    }
}
