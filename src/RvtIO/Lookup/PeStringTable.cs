namespace RvtIO.Lookup;

/// <summary>Reads the RT_STRING (type 6) table of a Windows PE file for one resource
/// language without loading or executing it. Every offset, count and string length is
/// bounds-checked; a malformed file yields null rather than a partial table.</summary>
internal static class PeStringTable
{
    private const int ResourceDirectoryIndex = 2;
    private const uint StringResourceType = 6;

    public static IReadOnlyDictionary<int, string>? Read(byte[] image, ushort languageId)
    {
        try
        {
            return ReadCore(image, languageId);
        }
        catch (FormatException) { return null; }
        catch (ArgumentException) { return null; }
    }

    private static Dictionary<int, string> ReadCore(byte[] image, ushort languageId)
    {
        if (U16(image, 0) != 0x5A4D) throw new FormatException("MZ");
        int pe = checked((int)U32(image, 0x3C));
        if (U32(image, pe) != 0x00004550) throw new FormatException("PE");
        int coff = pe + 4;
        int sectionCount = U16(image, coff + 2);
        int optionalSize = U16(image, coff + 16);
        int optional = coff + 20;
        int directories = U16(image, optional) switch
        {
            0x10B => optional + 96,
            0x20B => optional + 112,
            _ => throw new FormatException("optional header"),
        };
        if (directories + 8 * (ResourceDirectoryIndex + 1) > optional + optionalSize)
            throw new FormatException("data directories");
        uint resourceRva = U32(image, directories + 8 * ResourceDirectoryIndex);
        if (resourceRva == 0) throw new FormatException("no resources");

        var sections = new List<(uint Va, uint Size, uint Raw, uint RawSize)>();
        int sectionTable = optional + optionalSize;
        for (int index = 0; index < sectionCount; index++)
        {
            int s = sectionTable + 40 * index;
            sections.Add((U32(image, s + 12), U32(image, s + 8), U32(image, s + 20), U32(image, s + 16)));
        }
        int Offset(uint rva, uint length)
        {
            // Resource bytes must be present in the file image, so only the raw extent counts.
            foreach (var (va, _, raw, rawSize) in sections)
            {
                if (rva >= va && (ulong)rva + length <= (ulong)va + rawSize)
                    return checked((int)(raw + (rva - va)));
            }
            throw new FormatException("rva");
        }

        int root = Offset(resourceRva, 16);
        var result = new Dictionary<int, string>();
        foreach (var (typeId, typeOffset, typeIsDirectory) in Entries(image, root))
        {
            if (typeId != StringResourceType) continue;
            if (!typeIsDirectory) throw new FormatException("type leaf");
            foreach (var (blockId, blockOffset, blockIsDirectory) in Entries(image, root + typeOffset))
            {
                if (!blockIsDirectory || blockId is 0 or > 4096) throw new FormatException("block");
                foreach (var (language, dataOffset, languageIsDirectory) in Entries(image, root + blockOffset))
                {
                    if (language != languageId) continue;
                    if (languageIsDirectory) throw new FormatException("language directory");
                    int entry = root + dataOffset;
                    uint dataRva = U32(image, entry);
                    uint dataSize = U32(image, entry + 4);
                    int data = Offset(dataRva, dataSize);
                    int end = data + (int)dataSize;
                    int position = data;
                    for (int slot = 0; slot < 16; slot++)
                    {
                        if (position + 2 > end) throw new FormatException("string length");
                        int length = U16(image, position);
                        position += 2;
                        if (position + 2 * length > end) throw new FormatException("string data");
                        if (length > 0)
                        {
                            int id = checked((int)(blockId - 1) * 16 + slot);
                            if (!result.TryAdd(id, System.Text.Encoding.Unicode.GetString(image, position, 2 * length)))
                                throw new FormatException("duplicate id");
                        }
                        position += 2 * length;
                    }
                }
            }
        }
        return result;
    }

    private static IEnumerable<(uint Id, int Offset, bool IsDirectory)> Entries(byte[] image, int directory)
    {
        int named = U16(image, directory + 12);
        int ids = U16(image, directory + 14);
        var entries = new List<(uint, int, bool)>(named + ids);
        for (int index = 0; index < named + ids; index++)
        {
            int entry = directory + 16 + 8 * index;
            uint name = U32(image, entry);
            uint target = U32(image, entry + 4);
            if (index < named) continue;
            if ((name & 0x80000000) != 0) throw new FormatException("named id entry");
            entries.Add((name, (int)(target & 0x7FFFFFFF), (target & 0x80000000) != 0));
        }
        return entries;
    }

    private static ushort U16(byte[] image, int offset) =>
        offset < 0 || offset > image.Length - 2 ? throw new FormatException("bounds")
            : BitConverter.ToUInt16(image, offset);

    private static uint U32(byte[] image, int offset) =>
        offset < 0 || offset > image.Length - 4 ? throw new FormatException("bounds")
            : BitConverter.ToUInt32(image, offset);
}
