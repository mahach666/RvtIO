using RvtIO.Compact;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The Design Option parameter of a document with design option sets: the header of the element names its option, the null id
/// prints "None".</summary>
public class CompactDesignOptionTests
{
    private const int DesignOption = -1013201;

    private static string? Text(int? headerOption, bool optionSetsExist, DesignOptions? options = null, int[]? ids = null, Element? own = null)
    {
        var element = own ?? new Element { Id = 10, NativeRootClassName = "GStyleElem", NativeHeaderDesignOptionId = headerOption };
        var info = new RevitFileInfo();
        info.Elements.Add(element);
        var parameters = new CompactBuiltinParameters(new CompactElementNames(info, null), null, new CompactListFileFacts(false, true, optionSetsExist, options),
            _ => -2000000, CompactValueGate.Parse(""), null) { IdListOverride = _ => ids ?? [DesignOption] };
        return parameters.GetEntries(element).Entries.Select(entry => entry.Property.Value).SingleOrDefault();
    }
    private static DesignOptions Options() => DesignOptions.Of(
    [
        new Element { Id = 875634, NativePrimaryDesignOptionId = 875635, NativeDesignOptionName = "Option Set 1" },
        new Element { Id = 875635, NativeDesignOptionSetId = 875634, NativeDesignOptionName = "Option 1" },
        new Element { Id = 875638, NativeDesignOptionSetId = 875634, NativeDesignOptionName = "Option 2" },
    ]);

    [Fact]
    public void ElementInAnOption_PrintsTheNameOfItsOption()
    {
        Assert.Equal("Option 2", Text(875638, optionSetsExist: true, Options()));
        Assert.Equal("Option 1", Text(875635, optionSetsExist: true, Options()));
        Assert.Null(Text(999, optionSetsExist: true, Options()));
    }

    [Fact]
    public void OptionAndSetRecords_PrintTheirNamesAndRelations()
    {
        var set = new Element { Id = 875634, NativePrimaryDesignOptionId = 875635, NativeDesignOptionName = "Option Set 1" };
        var option = new Element { Id = 875638, NativeDesignOptionSetId = 875634, NativeDesignOptionName = "Option 2" };
        Assert.Equal("Option Set 1", Text(null, true, Options(), [-1133600], set));
        Assert.Equal("Option 1", Text(null, true, Options(), [-1133601], set));
        Assert.Equal("Option 2", Text(null, true, Options(), [-1133602], option));
        Assert.Equal("Option Set 1", Text(null, true, Options(), [-1133603], option));
        Assert.Null(Text(null, true, null, [-1133603], option));
    }

    [Fact]
    public void NonPrimaryOption_IsDecidedOnlyWhenTheRecordsResolveIt()
    {
        var options = Options();
        Element At(int option) => new() { Id = 1, NativeHeaderDesignOptionId = option };
        Assert.False(options.InNonPrimaryOption(At(-1)));
        Assert.False(options.InNonPrimaryOption(At(875635)));
        Assert.True(options.InNonPrimaryOption(At(875638)));
        Assert.Null(options.InNonPrimaryOption(At(777)));
        Assert.Null(options.InNonPrimaryOption(new Element { Id = 1 }));
        var undecided = DesignOptions.Of([new Element { Id = 875634 }, new Element { Id = 875638, NativeDesignOptionSetId = 875634 }]);
        Assert.Null(undecided.InNonPrimaryOption(At(875638)));
        var conflicting = DesignOptions.Of([
            new Element { Id = 875634, NativePrimaryDesignOptionId = 875635 }, new Element { Id = 875638, NativeDesignOptionSetId = 875634 },
            new Element { Id = 875638, NativeDesignOptionSetId = 5 }]);
        Assert.Null(conflicting.InNonPrimaryOption(At(875638)));
    }

    [Fact]
    public void NullOption_PrintsNone_WithOrWithoutOptionSets()
    {
        Assert.Equal("None", Text(-1, optionSetsExist: true));
        Assert.Equal("None", Text(-1, optionSetsExist: false));
    }

    [Fact]
    public void ElementInAnOption_IsLeftOutBecauseTheNameIsNotValidated()
        => Assert.Null(Text(875638, optionSetsExist: true));

    [Fact]
    public void UnknownHeaderOption_WithOptionSets_IsLeftOut()
        => Assert.Null(Text(null, optionSetsExist: true));

    [Fact]
    public void NoOptionSets_PrintNoneWithoutAHeader()
        => Assert.Equal("None", Text(null, optionSetsExist: false));
}
