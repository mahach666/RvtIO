using System.Text;

namespace RvtIO.Ifc;

public partial class IfcExporter
{
    private static readonly UTF8Encoding StrictPropertyUtf8 = new(false, true);

    /// <summary>Label and identifier values are UTF-8 and keep at most 255 bytes.
    /// IfcText and product identity strings have no such limit.</summary>
    internal static int ApplyCurrentStringPropertyByteLimits(StepWriter writer)
    {
        int rewritten = 0;
        for (int i = 0; i < writer.EntityCount; i++)
        {
            var (_, type, args) = writer.EntityAt(i);
            if (type != "IFCPROPERTYSINGLEVALUE") continue;
            string[] attributes = SplitStepArguments(args);
            if (attributes.Length != 4) continue;
            string expression = attributes[2];
            int open = expression.IndexOf('(');
            if (open <= 0 || expression[..open] is not ("IFCLABEL" or "IFCIDENTIFIER")
                || !expression.EndsWith("')") || expression.Length <= open + 3
                || expression[open + 1] != '\'') continue;
            string? text = StepWriter.Unescape(expression[(open + 2)..^2]);
            if (text == null) continue;
            byte[] bytes;
            try { bytes = StrictPropertyUtf8.GetBytes(text); }
            catch (EncoderFallbackException) { continue; }
            if (bytes.Length <= 255) continue;
            // shortcut: retain split UTF-8 prefixes until native code-page fallback is supported.
            if ((bytes[255] & 0xC0) == 0x80) continue;
            attributes[2] = expression[..(open + 1)] + StepWriter.Str(StrictPropertyUtf8.GetString(bytes, 0, 255)) + ")";
            writer.ReplaceArgs(i, string.Join(",", attributes));
            rewritten++;
        }
        return rewritten;
    }
}
