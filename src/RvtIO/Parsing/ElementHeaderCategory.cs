using RvtIO.Internal;

namespace RvtIO.Parsing;

/// <summary>
/// ElementHeader.m_categroryId of an element record: the category id that Revit
/// reports as the header category of an element. Every element record starts with the serialised
/// ElementHeader (schema class 1340).
/// </summary>
internal static class ElementHeaderCategory
{
    private const int RegenCountOffset = 10;
    private const int RegenEntryLength = 14;

    /// <summary>
    /// Reads m_categroryId of the record that starts at <paramref name="recordStart"/>; null when
    /// the bytes do not form a complete ElementHeader record (class tag, declared size or entry
    /// count do not fit the buffer).
    /// </summary>
    public static int? Read(byte[] buffer, int recordStart)
    {
        if (recordStart < 0 || (long)recordStart + RegenCountOffset + 4 > buffer.Length) return null;
        ushort recordClass = BitConverter.ToUInt16(buffer, recordStart + 8);
        if (recordClass is not (0x053C or 0x04F8)) return null;
        long recordEnd = (long)recordStart + 12 + BitConverter.ToUInt32(buffer, recordStart + 4);
        long categoryOffset = (long)recordStart + RegenCountOffset + 4
            + RegenEntryLength * (long)BitConverter.ToUInt32(buffer, recordStart + RegenCountOffset);
        if (recordEnd > buffer.Length || categoryOffset + 4 > recordEnd) return null;
        return BitConverter.ToInt32(buffer, (int)categoryOffset);
    }

    /// <summary>
    /// Reads ElementHeader.m_designOptionId of the same record: the fields m_familyId and
    /// m_ownerViewId (int32 each) lie between the category and the design option.
    /// </summary>
    public static int? ReadDesignOption(byte[] buffer, int recordStart)
    {
        if (Read(buffer, recordStart) is null) return null;
        long recordEnd = (long)recordStart + 12 + BitConverter.ToUInt32(buffer, recordStart + 4);
        long optionOffset = (long)recordStart + RegenCountOffset + 4
            + RegenEntryLength * (long)BitConverter.ToUInt32(buffer, recordStart + RegenCountOffset) + 12;
        return optionOffset + 4 <= recordEnd ? BitConverter.ToInt32(buffer, (int)optionOffset) : null;
    }

    /// <summary>
    /// Stores the header category and design option of a record the anchored stream walk accepted.
    /// An id whose records disagree (or whose header cannot be read) is stored as null, which stays
    /// null: unknown is never turned into a guess.
    /// </summary>
    public static void Record(ParsingState state, byte[] buffer, int recordStart, uint elementId)
    {
        int? category = Read(buffer, recordStart);
        if (!state.HeaderCategories.TryGetValue(elementId, out int? previous)) state.HeaderCategories[elementId] = category;
        else if (previous != category) state.HeaderCategories[elementId] = null;
        int? option = ReadDesignOption(buffer, recordStart);
        if (!state.HeaderDesignOptions.TryGetValue(elementId, out int? previousOption)) state.HeaderDesignOptions[elementId] = option;
        else if (previousOption != option) state.HeaderDesignOptions[elementId] = null;
    }
}
