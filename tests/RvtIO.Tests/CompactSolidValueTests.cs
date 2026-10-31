using RvtIO.Compact;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The values of a file of a wide layout that come from the owner view of a curve (Detail Line), from the curve length of the Revit 2022
/// reader (Length of a curve element) and from the solids (Volume, Area, the empty text) as <see cref="CompactBuiltinParameters"/> writes them, with the
/// format settings of the real file Test_2022.</summary>
public class CompactSolidValueTests
{
    private const int Volume = -1012806, Area = -1012805, SystemLength = -1001375, Length = -1004005, DetailLine = -1004011;
    private const int CurtainMullions = -2000171, StructuralFraming = -2001320;

    private const string CurveGate = "O\tCurveElem\nC\tCurveElem\t-1004011\t1\nC\tCurveElem\t-1004005\t1\n";

    private const string SolidGate = "V\tFamilyInstance\t-1012806\t-2000171\tsolid\tvolume\t9\nV\tFamilyInstance\t-1012805\t-2000171\tsolid\thalf\t9\n"
        + "V\tFamilyInstance\t-1012806\t-2000171\tnone\tempty\t9\nV\tFamilyInstance\t-1001375\t-2001300\tany\tempty\t9\n";

    private static readonly Lazy<CompactFormatSettings> SharedSettings = new(() =>
        new RvtFileReader().Read(TestFileFixture.FindTestFile("Test_2022.rvt")).SerializedFormatSettings
        ?? throw new InvalidOperationException("Test_2022 has no format settings"));

    private static CompactFormatSettings Settings() => SharedSettings.Value;

    private static IReadOnlyDictionary<int, string> Written(string rootClass, int header, string gate, CompactSolidQuantities? quantities, int[] ids,
        int ownerView = -1, Element? element = null)
    {
        element ??= new Element { Id = 10, NativeRootClassName = rootClass, SerializedOwnerDbViewId = ownerView, NativeParameterStoresComplete = true, NativeParameterStoreIds = [] };
        var state = new ParsingState();
        var info = new RevitFileInfo();
        info.Elements.Add(element);
        var parameters = new CompactBuiltinParameters(new CompactElementNames(info, state), Settings(), new CompactListFileFacts(null, true, false),
            _ => header, CompactValueGate.Parse(gate), state, null, null, quantities) { IdListOverride = _ => ids };
        return parameters.GetEntries(element).Entries.ToDictionary(e => e.Id, e => e.Property.Value);
    }

    [SkippableTheory]
    [InlineData(5, "Yes")]
    [InlineData(-1, "No")]
    public void DetailLine_IsYesExactlyWhenAViewOwnsTheCurve(int ownerView, string expected)
    {
        var result = Written("CurveElem", -2000051, CurveGate, null, [DetailLine], ownerView);

        Assert.Equal(expected, result[DetailLine]);
    }

    [SkippableFact]
    public void CurveLength_IsTheOneOfTheRevit2022Reader_AndNothingWithoutIt()
    {
        var quantities = CompactSolidQuantities.Of([], [(10, 6.0)]);

        string text = Written("CurveElem", -2000051, CurveGate, quantities, [Length])[Length];

        Assert.Equal(CompactValueFormatter.Format(6.0, ParameterSpec(Length), Settings()), text);
        Assert.Empty(Written("CurveElem", -2000051, CurveGate, CompactSolidQuantities.Of([], []), [Length]));
        Assert.Empty(Written("CurveElem", -2000051, CurveGate, null, [Length]));
    }

    private static string ParameterSpec(int id)
    {
        Assert.True(ApiParameterDefinitions.TryGet(id, out _, out _, out string spec));
        return spec;
    }

    private static CompactSolidQuantities.Solids Solid(string shape, double volume = 0, double area = 0, int category = CurtainMullions)
        => new(shape, 2, shape == "none" ? 0 : 1, volume, area, 0, null, category);

    [SkippableFact]
    public void SolidRules_WriteTheFormulaOfTheCategory_AndTheEmptyTextWithoutASolid()
    {
        var settings = Settings();
        var one = CompactSolidQuantities.Of([(10, Solid("one", 36, 72))], []);
        var result = Written("FamilyInstance", CurtainMullions, SolidGate, one, [Volume, Area]);

        Assert.Equal(CompactValueFormatter.Format(36, ParameterSpec(Volume), settings), result[Volume]);
        Assert.Equal(CompactValueFormatter.Format(36, ParameterSpec(Area), settings), result[Area]);       // half of the surface

        var none = Written("FamilyInstance", CurtainMullions, SolidGate, CompactSolidQuantities.Of([(10, Solid("none"))], []), [Volume, Area]);
        Assert.Equal("", none[Volume]);
        Assert.False(none.ContainsKey(Area));                                                          // no rule for Area without a solid
    }

    [SkippableFact]
    public void SolidRules_WriteNothing_ForAShapeOrCategoryWithoutALine_OrWithoutTheSecondRead()
    {
        Assert.Empty(Written("FamilyInstance", CurtainMullions, SolidGate, CompactSolidQuantities.Of([(10, Solid("other", 36, 72))], []), [Volume, Area]));
        Assert.Empty(Written("FamilyInstance", StructuralFraming, SolidGate, CompactSolidQuantities.Of([(10, Solid("one", 36, 72, StructuralFraming))], []), [Volume]));
        Assert.Empty(Written("FamilyInstance", CurtainMullions, SolidGate, null, [Volume]));
        Assert.Empty(Written("FamilyInstance", CurtainMullions, SolidGate, CompactSolidQuantities.Of([], []), [Volume]));
    }

    [SkippableFact]
    public void SystemLength_OfAFoundation_IsEmptyWhateverTheSolids()
    {
        var result = Written("FamilyInstance", -2001300, SolidGate, CompactSolidQuantities.Of([(10, Solid("other", 1, 1, -2001300))], []), [SystemLength]);

        Assert.Equal("", result[SystemLength]);
    }
}
