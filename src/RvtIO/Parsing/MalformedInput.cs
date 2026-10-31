using ICSharpCode.SharpZipLib;

namespace RvtIO.Parsing;

/// <summary>Classifies the failures a speculative parse of untrusted file bytes may raise.
/// Anything outside this set (out of memory, null reference, programming errors) is not
/// swallowed and propagates.</summary>
internal static class MalformedInput
{
    /// <summary>True when <paramref name="ex"/> means the probed bytes are not in the shape being tried.</summary>
    internal static bool Is(Exception ex) => ex switch
    {
        AggregateException aggregate => aggregate.InnerExceptions.All(Is),
        IOException or InvalidDataException or SharpZipBaseException or ArgumentException
            or IndexOutOfRangeException or FormatException or OverflowException
            or KeyNotFoundException or InvalidOperationException or NotSupportedException
            or System.Xml.XmlException => true,
        _ => false,
    };
}
