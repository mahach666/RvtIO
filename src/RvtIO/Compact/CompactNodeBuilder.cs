using RvtIO.Model;
using RvtIO.Parsing;

namespace RvtIO.Compact;

/// <summary>
/// Builds the identity part of a compact JSON node from an element: the id, the class name, the unique id, and the category
/// label and family name of the two objects in front of it (<see cref="CompactIdentity"/>).
/// </summary>
internal sealed class CompactNodeBuilder
{
    /// <summary>
    /// Schema class names whose class name is not the schema name: NestedFamilyTypeReference for NestedFamSymRefElem,
    /// PathReinforcement for PathRein and DisplacementPath for DisplacementElementLocationLine.
    /// </summary>
    private static readonly Dictionary<string, string> RenamedSchemaClasses = new(StringComparer.Ordinal)
    {
        ["NestedFamSymRefElem"] = "NestedFamilyTypeReference",
        ["PathRein"] = "PathReinforcement",
        ["DisplacementElementLocationLine"] = "DisplacementPath",
    };

    private readonly Dictionary<string, int> _unresolvedClassNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _unknownPseudo = new(StringComparer.Ordinal);
    private readonly CompactIdentity _identity;

    /// <param name="identity">The identity rules; without them only the rules that need no other element apply.</param>
    public CompactNodeBuilder(CompactIdentity? identity = null) => _identity = identity ?? new CompactIdentity([]);

    /// <summary>Number of nodes built with an empty class name.</summary>
    public int UnresolvedClassNameCount { get; private set; }

    /// <summary>Why the class name of a node stayed empty ("no root class id", "class id N has no schema name") and how many
    /// nodes it hit.</summary>
    public IReadOnlyDictionary<string, int> UnresolvedClassNames => _unresolvedClassNames;

    /// <summary>Number of nodes written without the two preceding objects because it could not be decided whether they belong.</summary>
    public int UnknownPseudoCount { get; private set; }

    /// <summary>Why the pseudo objects of a node were undecided and how many nodes it hit.</summary>
    public IReadOnlyDictionary<string, int> UnknownPseudo => _unknownPseudo;

    /// <summary>Number of nodes built with an empty unique id.</summary>
    public int MissingUniqueIdCount { get; private set; }

    public CompactNode Build(Element element, IReadOnlyList<CompactProperty> properties)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(properties);
        string? className = ClassNameOf(element, out string? reason);
        if (className == null)
        {
            UnresolvedClassNameCount++;
            _unresolvedClassNames[reason!] = _unresolvedClassNames.GetValueOrDefault(reason!) + 1;
        }
        if (string.IsNullOrEmpty(element.UniqueId)) MissingUniqueIdCount++;
        CompactPseudo pseudo = _identity.Pseudo(element);
        if (!pseudo.IsKnown)
        {
            UnknownPseudoCount++;
            string why = _identity.FamilyName(element).Unknown ?? _identity.CategoryLabel(element).Unknown ?? "undecided";
            _unknownPseudo[why] = _unknownPseudo.GetValueOrDefault(why) + 1;
        }
        return new CompactNode(element.Id, className ?? "", element.UniqueId ?? "", pseudo.CategoryLabel, pseudo.FamilyName, properties);
    }

    /// <summary>The class name for the property rules (<see cref="Element.RuleClassName"/>): the same as <see cref="ClassNameOf"/>.</summary>
    internal static string? ResolveClassName(Element element, out string? reason) => ClassNameOf(element, out reason);

    /// <summary>The schema name of the element's own root class in every layout (or the spelling of the classes of
    /// <see cref="RenamedSchemaClasses"/>); null with the reason when the element has no root class or the schema
    /// has no such class.</summary>
    internal static string? ClassNameOf(Element element, out string? reason)
    {
        reason = null;
        if (string.IsNullOrEmpty(element.RootClassName))
        {
            reason = element.SerializedOwnElementClassId is ushort classId ? $"class id {classId} has no schema name" : "no root class id";
            return null;
        }
        return RenamedSchemaClasses.TryGetValue(element.RootClassName, out string? referenceName) ? referenceName : element.RootClassName;
    }
}
