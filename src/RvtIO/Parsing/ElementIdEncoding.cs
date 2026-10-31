namespace RvtIO.Parsing;

/// <summary>
/// Element identifiers stored by the supported RVT schemas are unsigned 32-bit values on disk,
/// while the public model currently exposes <see cref="int"/>.
/// </summary>
internal static class ElementIdEncoding
{
    public static bool IsSupported(uint value) => value is > 0 and <= int.MaxValue;
}
