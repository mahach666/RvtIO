using RvtIO.Internal;
using RvtIO.Model;

namespace RvtIO.Compact;

internal sealed class CompactPropertySources : ICompactPropertySource
{
    private readonly CompactBuiltinParameters _builtin;
    private readonly CompactIfcParameters _ifc;
    private readonly CompactUserParameters _user;
    private readonly CompactIdentityProperties? _identity;

    internal CompactPropertySources(CompactBuiltinParameters builtin, CompactIfcParameters ifc, CompactUserParameters user, CompactIdentity? identity = null)
    {
        _builtin = builtin;
        _ifc = ifc;
        _user = user;
        // the type entries of the identity block are listed by the same id list as the built-in entries
        _identity = identity == null ? null : new CompactIdentityProperties(identity, _ => _builtin.LastIds);
    }

    internal Action<Element, CompactProperty, CompactProperty>? Collision { get; set; }
    internal Action<Element, RvtIO.Parsing.UserParameterEntry>? UserTrace { set => _user.Trace = value; }
    internal CompactBuiltinParameters Builtin => _builtin;

    /// <summary>The composite of a file.</summary>
    internal static CompactPropertySources Create(RevitFileInfo info, ParsingState? state, bool? isProjectDocument,
        Func<Element, int?>? headerCategory = null, bool gated = true, CompactIdentity? identity = null, CompactSolidQuantities? quantities = null)
    {
        ArgumentNullException.ThrowIfNull(info);
        var names = new CompactElementNames(info, state);
        var file = new CompactListFileFacts(info.SerializedIsWorkshared, isProjectDocument,
            info.Elements.Any(element => CompactIfcParameters.ClassName(element) == "DesignOptionSet"), DesignOptions.Of(info.Elements));
        double? surveyOffset = info.SharedTransformIsActiveLocation && info.SharedTransform is { Length: 12 } transform ? transform[11] : null;
        var builtin = new CompactBuiltinParameters(names, info.SerializedFormatSettings, file, headerCategory, gated ? CompactValueGate.ForLayout(info.GeometryUndecoded) : null,
            state, surveyOffset, info.SerializedLookupNoneText, quantities);
        var settings = info.SerializedFormatSettings;
        var user = new CompactUserParameters(info,
            settings == null ? null : (spec, raw) => CompactValueFormatter.Format(raw, spec, settings),
            names.NameOf);
        return new CompactPropertySources(builtin, new CompactIfcParameters(info), user, identity);
    }

    public IReadOnlyList<CompactProperty> GetProperties(Element element)
    {
        var builtin = _builtin.GetEntries(element, CompactIfcParameters.OwnedIds.Contains);
        var entries = new List<(int Id, CompactProperty Property)>(builtin.Entries);
        entries.AddRange(_ifc.GetEntries(element));
        if (_identity != null) entries.AddRange(_identity.GetEntries(element));
        var result = new List<CompactProperty>(entries.Count + 8);
        // a stable sort: equal ids cannot occur (the two sources own disjoint ids), but the order must not depend on it
        foreach (var entry in entries.OrderBy(entry => entry.Id)) result.Add(entry.Property);
        int builtinCount = result.Count;
        foreach (var (user, family) in _user.GetPropertiesWithKind(element))
        {
            if (!family && builtin.Unvalidated.Contains((user.Group, user.Label))) continue;
            int earlier = result.FindIndex(p => p.Group == user.Group && p.Label == user.Label);
            if (earlier >= 0)
            {
                Collision?.Invoke(element, result[earlier], user);
                // a parameter the family defines comes before the built-in parameters
                if (family && earlier < builtinCount) { result.RemoveAt(earlier); builtinCount--; }
            }
            result.Add(user);
        }
        return result;
    }
}
