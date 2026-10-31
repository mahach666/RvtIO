namespace RvtIO.Compact;

/// <summary>
/// The built-in parameters the framing user data of a family instance (StructFramUserData, kind 4 of <c>fi:userDataKind</c>) adds to
/// the list of FamilyInstance. Decided from the member's facts, none of them guessed: when a fact the choice needs is unknown, or
/// the member is not a beam with a curve driver, the contribution stays unmodelled (<see cref="Contribute"/> returns false).
/// <para>
/// Always listed: the justification set, the start and end extension, the four attachment ids, Structural Usage and the geometry
/// positioning ids. Cut Length with a curve driver; Start and End Connection and Stick Symbol Location with a symbolic
/// representation of the family; Camber Size and Number of studs only for structural material type 1; Coping Distance exactly when
/// the user data stores a coping distance; Start and End Attachment Type per end that has a FamInstStructuralAttachment (decided
/// only when no end has one).
/// </para>
/// </summary>
internal static class CompactFramingUserData
{
    private const int BeamStructuralType = 1;

    private static readonly int[] Always =
    [
        -1152365, -1152364, -1152363, -1152362, -1152361, -1152358, -1152357,   // justifications, offsets, start and end extension
        -1019016, -1019014, -1019012, -1019000,                                 // attachment parameters
        -1001381,                                                               // structural usage
    ];

    private const int CutLength = -1001384, CopingDistance = -1001559;
    private static readonly int[] WithSymbolicRepresentation = [-1018802, -1018801, -1001503];
    private static readonly int[] OnlyOfMaterialType1 = [-1001530, -1001529];

    /// <summary>Adds the ids of the member to <paramref name="ids"/> and the ids it takes out again to <paramref name="removed"/>;
    /// false (nothing added) when the member's facts do not decide the contribution.</summary>
    internal static bool Contribute(ICompactListFacts facts, ISet<int> ids, ISet<int> removed)
    {
        if (facts.Get("fi:hasCurveDriver") != 1 || facts.Get("fi:framingAttachment") != 0
            || facts.Get("fi:famSymbolicRepType") is not int representation || facts.Get("fi:famStructMaterialType") is not int material
            || facts.Get("fi:framingCoping") is not int coped || facts.Get("fi:structTypeFromSymbol") != BeamStructuralType)
            return false;
        foreach (int id in Always) ids.Add(id);
        ids.Add(CutLength);
        if (representation != 0) foreach (int id in WithSymbolicRepresentation) ids.Add(id);
        if (coped != 0) ids.Add(CopingDistance);
        if (material != 1) foreach (int id in OnlyOfMaterialType1) removed.Add(id);
        return true;
    }
}
