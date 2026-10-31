using System.IO.Compression;
using OpenMcdf;

namespace RvtIO.Parsing;

internal static class ProjectInfoReader
{
    public static Dictionary<string, string> Read(RootStorage root)
    {
        var result = new Dictionary<string, string>();
        try
        {
            using var stream = root.OpenStream("ProjectInformation");
            var data = new byte[stream.Length];
            stream.ReadExactly(data);

            int zipStart = -1;
            for (int i = 0; i < data.Length - 3; i++)
                if (data[i] == 0x50 && data[i + 1] == 0x4B && data[i + 2] == 0x03 && data[i + 3] == 0x04)
                { zipStart = i; break; }
            if (zipStart < 0) return result;

            using var zipStream = new MemoryStream(data, zipStart, data.Length - zipStart);
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                if (!entry.Name.EndsWith(".xml")) continue;
                using var sr = new StreamReader(entry.Open());
                var xml = sr.ReadToEnd();

                // Russian element names -> English keys
                var nameMap = new Dictionary<string, string>
                {
                    ["Наименование_проекта"] = "Name",
                    ["Номер_проекта"] = "Number",
                    ["Заказчик"] = "Client",
                    ["Адрес_проекта"] = "Address",
                    ["Статус_проекта"] = "Status",
                    ["Дата_утверждения_проекта"] = "IssueDate",
                    ["Разработал"] = "Author",
                    ["Наименование_здания"] = "BuildingName",
                    ["Название_организации"] = "OrganizationName"
                };

                foreach (var (ruName, enName) in nameMap)
                {
                    // Find <RuName ...>value</RuName>
                    int tagStart = xml.IndexOf($"<{ruName}");
                    if (tagStart < 0) continue;
                    int valStart = xml.IndexOf('>', tagStart) + 1;
                    int valEnd = xml.IndexOf($"</{ruName}>", valStart);
                    if (valStart > 0 && valEnd > valStart)
                    {
                        string val = xml[valStart..valEnd].Trim();
                        if (val.Length > 0) result[enName] = val;
                    }
                }

                // Also extract from Atom metadata
                foreach (var tag in new[] { "title", "A:product-version" })
                {
                    int idx = xml.IndexOf($"<{tag}>");
                    if (idx >= 0)
                    {
                        int vs = idx + tag.Length + 2;
                        int ve = xml.IndexOf($"</{tag}>", vs);
                        if (ve > vs)
                        {
                            string val = xml[vs..ve].Trim();
                            if (tag == "title" && !result.ContainsKey("Title"))
                                result["Title"] = val;
                            else if (tag.Contains("version"))
                                result["RevitVersion"] = val;
                        }
                    }
                }
            }
        }
        // Unreadable or malformed project information: keep what was parsed.
        catch (Exception ex) when (MalformedInput.Is(ex)) { }
        return result;
    }
}
