using System.Text;
using System.Text.RegularExpressions;
using OpenMcdf;

namespace RvtIO.Parsing;

internal static partial class BasicFileInfoReader
{
    private const string CloudModelKey = "IsSingleUserCloudModel";
    public static Dictionary<string, string> Read(RootStorage root)
    {
        if (!root.TryOpenStream("BasicFileInfo", out var stream)) return new Dictionary<string, string>();
        using (stream)
        {
            var data = new byte[stream.Length];
            stream.ReadExactly(data);
            return Parse(data);
        }
    }

    internal static Dictionary<string, string> Parse(byte[] data)
    {
        var result = new Dictionary<string, string>();
        if (data.Length < 12) return result;

        int version = BitConverter.ToInt32(data, 0);
        result["StreamVersion"] = version.ToString();
        for (int pos = 4; pos + 4 <= data.Length;)
        {
            int charCount = BitConverter.ToInt32(data, pos);
            if (charCount > 0 && charCount < 2000 && pos + 4 + charCount * 2 <= data.Length
                && data[pos + 5] == 0 && data[pos + 4] >= 0x20)
            {
                string val = Encoding.Unicode.GetString(data, pos + 4, charCount * 2);
                if (val.Contains(":\\") || val.Contains(":/"))
                    result["LastSavePath"] = val;
                else if (val.Length == 4 && val.All(char.IsDigit))
                    result.TryAdd("RevitVersion", val);
                else if (val.Length > 8 && (val.Contains("(x64)") || val.Contains("(x86)")))
                    result["Build"] = val;
                pos += 4 + charCount * 2;
            }
            else
            {
                pos++;
            }
        }
        for (int parity = 0; parity < 2; parity++)
        {
            int runStart = -1;
            for (int i = parity; i < data.Length - 1; i += 2)
            {
                byte lo = data[i], hi = data[i + 1];
                bool isText = (hi == 0 && lo >= 0x20 && lo <= 0x7E) || (hi == 0 && (lo == 0x09 || lo == 0x0A || lo == 0x0D));
                if (isText) { if (runStart < 0) runStart = i; }
                else
                {
                    if (runStart >= 0 && i - runStart >= 8)
                    {
                        var sb = new StringBuilder();
                        for (int j = runStart; j < i; j += 2) sb.Append((char)data[j]);
                        string run = sb.ToString();
                        bool anyKv = false;
                        foreach (var lineRaw in run.Split('\r', '\n'))
                        {
                            string line = lineRaw.Trim();
                            int ci = line.IndexOf(":\t");
                            if (ci < 0) ci = line.IndexOf(": ");
                            if (ci > 0 && ci < 60)
                            {
                                string key = line[..ci].Trim();
                                string val = line[(ci + 2)..].Trim();
                                if (key.Length > 0 && val.Length > 0 && !result.ContainsKey(key))
                                { result[key] = val; anyKv = true; }
                            }
                        }
                        if (!anyKv && run.Contains("Revit") && !result.ContainsKey("FullBuild"))
                            result["FullBuild"] = run.Trim();
                    }
                    runStart = -1;
                }
            }
        }
        ReadCloudModel(data, result);
        if (result.TryGetValue("Revit Build", out string? labelledBuild)) result.TryAdd("Build", labelledBuild);
        if (result.TryGetValue("Format", out string? format)
            && format.Length == 4 && format.All(char.IsDigit))
            result["RevitVersion"] = format;
        if (!result.ContainsKey("RevitVersion") && ReleaseOfBuild(result) is { } release)
            result["RevitVersion"] = release;
        return result;
    }
    private static void ReadCloudModel(byte[] data, Dictionary<string, string> result)
    {
        var key = Encoding.Unicode.GetBytes(CloudModelKey + ": ");
        int at = data.AsSpan().IndexOf(key);
        if (at < 0) return;
        var value = data.AsSpan(at + key.Length);
        foreach (string text in new[] { "True", "False" })
            if (value.StartsWith(Encoding.ASCII.GetBytes(text + "\0")) || (value.StartsWith(Encoding.Unicode.GetBytes(text))
                && value.Length >= 2 * text.Length + 2 && value[2 * text.Length] is 0x0D or 0x0A))
                result[CloudModelKey] = text;
    }
    [GeneratedRegex(@"(?<!\d)((?:200[89]|20[1-3]\d))(?!\d)\s*\(Build:")]
    private static partial Regex YearBeforeBuild();
    private static string? ReleaseOfBuild(Dictionary<string, string> fields)
    {
        foreach (string key in new[] { "Build", "Revit Build", "FullBuild" })
        {
            if (!fields.TryGetValue(key, out string? text)) continue;
            var written = YearBeforeBuild().Match(text);
            if (written.Success) return written.Groups[1].Value;
        }
        return null;
    }
}
