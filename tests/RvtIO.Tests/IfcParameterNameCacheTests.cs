using System.Globalization;
using RvtIO.Ifc;
using Xunit;
using Fact = RvtIO.Ifc.IfcParameterNameCache.ParameterFact;
using Definition = RvtIO.Ifc.IfcParameterNameCache.DefinitionFact;

namespace RvtIO.Tests;

public class IfcParameterNameCacheTests
{
    private static Fact P(int id, string? name = "Total Thickness", int group = -10,
        bool hasValue = true, bool visible = true, bool internalDefinition = true)
        => new(id, new Definition(name, group, internalDefinition, visible), hasValue);
    private static IfcParameterNameCache Cache(params Fact?[] parameters)
        // These algorithm vectors use ASCII names (or ordinary case folding).
        // Original Framework/NLS Unicode equivalence is checked separately.
        => IfcParameterNameCache.Create(parameters, StringComparer.InvariantCultureIgnoreCase, true, out _) ?? throw new InvalidDataException();

    [Fact]
    public void IncompletePublicScopeCannotBecomeAnEmptyOrPartialSuccessfulCache()
    {
        Assert.Null(IfcParameterNameCache.Create([P(-1)], StringComparer.InvariantCultureIgnoreCase, false, out var failure));
        Assert.Contains("scope", failure);
        Assert.Null(IfcParameterNameCache.Create(null, StringComparer.InvariantCultureIgnoreCase, true, out _));
        var empty = IfcParameterNameCache.Create([], StringComparer.InvariantCultureIgnoreCase, true, out failure);
        Assert.NotNull(empty); Assert.Null(failure); Assert.Null(empty.Find("TotalThickness"));
    }

    [Fact]
    public void IfcGroupWinsEvenWhenItsSelectedCandidateHasNoValue()
    {
        var unsetIfc = P(100, group: IfcParameterNameCache.IfcGroup, hasValue: false);
        var other = P(-100, group: int.MinValue);
        var cache = Cache(other, unsetIfc);
        Assert.Same(unsetIfc, cache.Find("total thickness"));
        Assert.False(cache.Find("total thickness")!.HasValue);
        Assert.Same(other, cache.Find("total thickness", int.MinValue));
    }

    [Fact]
    public void FirstSignedNonIfcGroupWinsRegardlessOfHasValueOrParameterId()
    {
        var firstGroup = P(int.MaxValue, group: -200, hasValue: false);
        var secondGroup = P(int.MinValue, group: -100);
        var cache = Cache(secondGroup, firstGroup);
        Assert.Same(firstGroup, cache.Find("TotalThickness"));
        Assert.Same(secondGroup, cache.Find("TotalThickness", -100));
        Assert.Null(cache.Find("TotalThickness", -300));
        Assert.Null(cache.Find("TotalThickness", IfcParameterNameCache.IfcGroup));
    }

    [Theory]
    [InlineData(false, false, false, 30)]
    [InlineData(false, false, true, 30)]
    [InlineData(false, true, false, 20)]
    [InlineData(false, true, true, 20)]
    [InlineData(true, false, false, 10)]
    [InlineData(true, false, true, 10)]
    [InlineData(true, true, false, 10)]
    [InlineData(true, true, true, 10)]
    public void SameNameUsesFirstHasValueElseLastCandidate(bool first, bool second, bool third, int expected)
    {
        var cache = Cache(P(30, "TOTALTHICKNESS", hasValue: third),
            P(10, "Total Thickness", hasValue: first), P(20, "total thickness", hasValue: second));
        Assert.Equal(expected, cache.Find("TotalThickness")!.Id);
    }

    [Fact]
    public void SignedParameterIdOrderNotInputOrAbsoluteIdOrderDeterminesWinner()
    {
        var cache = Cache(P(1), P(-1), P(int.MaxValue), P(int.MinValue), P(0));
        Assert.Equal(int.MinValue, cache.Find("TotalThickness")!.Id);
    }

    [Fact]
    public void DuplicateIdLastValidInputWinsBeforeNameAndHasValueSelection()
    {
        var cache = Cache(P(-5, "Old Name"), P(-5, "New Name", hasValue: false), P(-5, "Filtered", visible: false));
        Assert.Null(cache.Find("OldName")); Assert.Null(cache.Find("Filtered"));
        Assert.False(cache.Find("NewName")!.HasValue);
    }

    [Theory]
    [InlineData(IfcParameterNameCache.DuplicateCategoryParameter)]
    [InlineData(IfcParameterNameCache.DuplicateOriginalCategoryParameter)]
    public void OriginalRedundantBuiltinIdsAreExcludedEvenWithValueAndIfcGroup(int id)
    {
        var wanted = P(1);
        Assert.Same(wanted, Cache(P(id, group: IfcParameterNameCache.IfcGroup), wanted).Find("TotalThickness"));
    }

    [Fact]
    public void NullParameterDefinitionAndInvisibleInternalDefinitionAreExcluded()
    {
        var visible = P(1);
        var cache = Cache(null, new Fact(-20, null, true), P(-10, visible: false), visible);
        Assert.Same(visible, cache.Find("TotalThickness"));
    }

    [Fact]
    public void NonInternalDefinitionDoesNotUseInternalVisibilityPredicate()
    {
        var foreign = P(-10, visible: false, internalDefinition: false);
        Assert.Same(foreign, Cache(P(1), foreign).Find("TotalThickness"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00a0")]
    public void MissingAndWhitespaceDefinitionNamesAreExcluded(string? name)
    {
        var wanted = P(1);
        Assert.Same(wanted, Cache(P(-20, name), wanted).Find("TotalThickness"));
    }

    [Theory]
    [InlineData("TotalThickness")]
    [InlineData(" total THICKNESS ")]
    [InlineData("T o t a l T h i c k n e s s")]
    public void OnlyAsciiSpacesAreRemovedBeforeInvariantCaseInsensitiveLookup(string name)
    {
        var wanted = P(1);
        Assert.Same(wanted, Cache(wanted).Find(name));
    }

    [Theory]
    [InlineData("Total_Thickness")]
    [InlineData("Total\tThickness")]
    [InlineData("Total\nThickness")]
    public void UnderscoresTabsAndNewlinesAreNotRemoved(string name)
    {
        Assert.Null(Cache(P(1)).Find(name));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("ru-RU")]
    [InlineData("tr-TR")]
    public void NameComparisonDoesNotUseCurrentCulture(string culture)
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var wanted = P(1, "FINISH");
            Assert.Same(wanted, Cache(wanted).Find("finish"));
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void LookupIsASnapshotOfEnumerationNotALiveListOrGlobalOwnerCache()
    {
        var wanted = P(1);
        var list = new List<Fact?> { wanted };
        var cache = IfcParameterNameCache.Create(list, StringComparer.InvariantCultureIgnoreCase, true, out _)!;
        list.Clear(); list.Add(P(-1, "Different"));
        Assert.Same(wanted, cache.Find("TotalThickness")); Assert.Null(cache.Find("Different"));
        Assert.Null(Cache().Find("TotalThickness"));
        Assert.Throws<ArgumentNullException>(() => cache.Find(null!));
    }

    [Fact]
    public void MissingSourceComparisonCannotSilentlyUseHostInvariantOrdinalOrCurrentCulture()
    {
        Assert.Null(IfcParameterNameCache.Create([P(1)], null, true, out var failure));
        Assert.Contains("comparison", failure);
    }

    [Fact]
    public void IndependentGroupedReferenceAgreesWithAllSignedGroupAndValuePatterns()
    {
        // An independent batch formulation: select last eligible equal-ID input,
        // then each name-group's first valued candidate, or last unset candidate.
        int[] groups = [int.MinValue, IfcParameterNameCache.IfcGroup, -10, 0, int.MaxValue];
        string[] names = ["Thermal Transmittance", "THERMALTRANSMITTANCE", "other", "TotalThickness"];
        for (int mask = 0; mask < 256; mask++)
        {
            var inputs = Enumerable.Range(0, 8).Select(i => P(i - 4, names[i % names.Length],
                groups[i % groups.Length], (mask & (1 << i)) != 0)).AsEnumerable().Reverse().ToArray();
            var cache = Cache(inputs);
            var reference = inputs.GroupBy(p => p.Id).Select(g => g.Last()).OrderBy(p => p.Id)
                .GroupBy(p => p.Definition!.GroupId).ToDictionary(g => g.Key,
                    g => g.GroupBy(p => p.Definition!.Name!.Replace(" ", ""), StringComparer.InvariantCultureIgnoreCase)
                        .ToDictionary(n => n.Key, n => n.FirstOrDefault(p => p.HasValue) ?? n.Last(), StringComparer.InvariantCultureIgnoreCase));
            foreach (var name in names)
            {
                string clean = name.Replace(" ", "");
                foreach (int group in groups)
                    Assert.Same(reference.GetValueOrDefault(group)?.GetValueOrDefault(clean), cache.Find(name, group));
                var expected = reference.GetValueOrDefault(IfcParameterNameCache.IfcGroup)?.GetValueOrDefault(clean)
                    ?? reference.Where(g => g.Key != IfcParameterNameCache.IfcGroup).OrderBy(g => g.Key)
                        .Select(g => g.Value.GetValueOrDefault(clean)).FirstOrDefault(p => p != null);
                Assert.Same(expected, cache.Find(name));
            }
        }
    }
}
