namespace RvtIO.Cli;

/// <summary>The <c>--json-class-prefix &lt;text&gt;</c> option: a prefix for the class names the compact JSON export writes.
/// The option is read for every output kind and only the compact JSON export uses it.</summary>
internal static class JsonClassPrefixOption
{
    public const string Name = "--json-class-prefix";

    /// <summary>Reads the option from <paramref name="args"/>. Without the option <paramref name="prefix"/> is empty.
    /// False with <paramref name="error"/> set when the option has no value or is given twice.</summary>
    public static bool TryParse(string[] args, out string prefix, out string? error)
    {
        prefix = "";
        error = null;
        bool seen = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] != Name) continue;
            if (seen || i + 1 >= args.Length)
            {
                error = $"{Name} requires exactly one value (the class name prefix).";
                prefix = "";
                return false;
            }
            seen = true;
            prefix = args[++i];
        }
        return true;
    }
}
