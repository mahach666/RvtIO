using OpenMcdf;

namespace RvtIO.Parsing;

internal static class PreviewImageReader
{
    /// <summary>Extract preview image from RevitPreview4.0 stream as base64-encoded PNG or JPEG.</summary>
    public static (string? base64Data, string? mimeType) Read(RootStorage root)
    {
        try
        {
            using var stream = root.OpenStream("RevitPreview4.0");
            var raw = new byte[stream.Length];
            stream.ReadExactly(raw);

            // Scan for PNG signature (89 50 4E 47 0D 0A 1A 0A)
            for (int i = 0; i < raw.Length - 8; i++)
            {
                if (raw[i] == 0x89 && raw[i + 1] == 0x50 && raw[i + 2] == 0x4E && raw[i + 3] == 0x47)
                {
                    var imageData = raw.AsSpan(i).ToArray();
                    return (Convert.ToBase64String(imageData), "image/png");
                }
            }

            // Scan for JPEG signature (FF D8 FF)
            for (int i = 0; i < raw.Length - 3; i++)
            {
                if (raw[i] == 0xFF && raw[i + 1] == 0xD8 && raw[i + 2] == 0xFF)
                {
                    var imageData = raw.AsSpan(i).ToArray();
                    return (Convert.ToBase64String(imageData), "image/jpeg");
                }
            }
        }
        // No readable preview stream: report no image.
        catch (Exception ex) when (MalformedInput.Is(ex)) { }

        return (null, null);
    }
}
