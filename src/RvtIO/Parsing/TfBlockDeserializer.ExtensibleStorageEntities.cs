namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    private const int ExtensibleStorageEntityLength = 16 + 4 + 16;

    /// <summary>ESEntityCell.m_entityMap is a map of std::pair&lt;GUIDvalue, ESEntity&gt; whose pair has no layout in the recovered
    /// schema, so the walk of the deferred objects of a family instance would stop there and never reach its connector manager
    /// (no ports and no system membership). The entity of the pipe-calculation schema is the schema GUID, 0xFFFFFFFF and the
    /// schema GUID again, 36 bytes, followed directly by the next cell. Returns the position after a map of 1 to 16 such entities
    /// starting at <paramref name="position"/>; null for any other layout (a schema with stored values), which stays with the generic reader.</summary>
    internal static int? SkipEmptyExtensibleStorageEntities(byte[] data, int position, int end)
    {
        if (position < 0 || position + 4 > end) return null;
        uint entries = BitConverter.ToUInt32(data, position);
        if (entries is 0 or > 16 || position + 4L + ExtensibleStorageEntityLength * entries > end) return null;
        position += 4;
        for (uint i = 0; i < entries; i++, position += ExtensibleStorageEntityLength)
            if (BitConverter.ToUInt32(data, position + 16) != 0xFFFFFFFF
                || !data.AsSpan(position, 16).SequenceEqual(data.AsSpan(position + 20, 16)))
                return null;
        return position;
    }

    /// <summary>MEPAnalyticalFittingData (2367) stores m_ESFieldName and m_ESFieldValue, then the connector data array. A fitting
    /// with a stored K-factor table name carries one more string between them (for example "Tee", "PipeFittingKFactorTableName", "Tee"),
    /// which the recovered schema does not name. Returns the position of the array: <paramref name="position"/>
    /// when the array starts there, the position after one string when it starts right after it, else <paramref name="position"/>.
    /// The array is recognized by its element pointers, each the anonymous pointer 0xFFFFFFFF with the class tag 2366
    /// (MEPAnalyticalFittingConnectorData).</summary>
    internal static int SkipExtraFittingDataString(byte[] data, int position, int end)
    {
        if (IsFittingConnectorArray(data, position, end) || position + 4 > end) return position;
        uint length = BitConverter.ToUInt32(data, position);
        if (length > 200) return position;
        int next = position + 4 + 2 * (int)length;
        return next <= end && IsFittingConnectorArray(data, next, end) ? next : position;
    }

    private static bool IsFittingConnectorArray(byte[] data, int position, int end)
    {
        const ushort ConnectorDataClass = 2366;
        if (position < 0 || position + 4 > end) return false;
        uint count = BitConverter.ToUInt32(data, position);
        if (count is 0 or > 64 || position + 4 + 6L * count > end) return false;
        for (int i = 0; i < count; i++)
        {
            int entry = position + 4 + 6 * i;
            if (BitConverter.ToUInt32(data, entry) != 0xFFFFFFFF || BitConverter.ToUInt16(data, entry + 4) != ConnectorDataClass)
                return false;
        }
        return true;
    }
}
