using RvtIO.Internal;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>
/// ElementHeader.m_designOptionId of an element record in the Revit 2022 layout: [id:4][bodySize:4][class:2][count:4]
/// [count x 14-byte entries][m_categroryId:4][m_familyId:4][m_ownerViewId:4][m_designOptionId:4]; see <see cref="ElementHeaderCategory"/>.
/// </summary>
public class ElementHeaderDesignOptionTests
{
    /// <summary>A hand-made record whose header fields after the entries are category, family, owner view and design option, then
    /// <paramref name="tail"/> filler bytes.</summary>
    private static byte[] Record(uint id, int option, int entries = 0, int category = -2000011, int family = 111, int ownerView = 222,
        ushort recordClass = 0x053C, int tail = 8)
    {
        int length = 14 + 14 * entries + 16 + tail;
        var bytes = new byte[length];
        BitConverter.GetBytes(id).CopyTo(bytes, 0);
        BitConverter.GetBytes((uint)(length - 12)).CopyTo(bytes, 4);
        BitConverter.GetBytes(recordClass).CopyTo(bytes, 8);
        BitConverter.GetBytes((uint)entries).CopyTo(bytes, 10);
        for (int i = 14; i < 14 + 14 * entries; i++) bytes[i] = 0xE1;
        int at = 14 + 14 * entries;
        BitConverter.GetBytes(category).CopyTo(bytes, at);
        BitConverter.GetBytes(family).CopyTo(bytes, at + 4);
        BitConverter.GetBytes(ownerView).CopyTo(bytes, at + 8);
        BitConverter.GetBytes(option).CopyTo(bytes, at + 12);
        for (int i = at + 16; i < length; i++) bytes[i] = 0xFF;
        return bytes;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(17)]
    public void ReadDesignOption_SkipsTheEntriesCategoryFamilyAndOwnerView(int entries)
    {
        Assert.Equal(88231, ElementHeaderCategory.ReadDesignOption(Record(5, 88231, entries), 0));
    }

    [Fact]
    public void ReadDesignOption_NoOptionIsMinusOne_AndTheCategoryDoesNotLeakIn()
    {
        var bytes = Record(6, -1, category: -2000032, family: 7, ownerView: 8);
        Assert.Equal(-1, ElementHeaderCategory.ReadDesignOption(bytes, 0));
        Assert.Equal(-2000032, ElementHeaderCategory.Read(bytes, 0));
    }

    [Fact]
    public void ReadDesignOption_ReadsTheRecordAtItsStartOffsetInALargerBuffer()
    {
        var first = Record(8, 100, 1);
        var second = Record(9, 200);
        var buffer = first.Concat(second).ToArray();
        Assert.Equal(100, ElementHeaderCategory.ReadDesignOption(buffer, 0));
        Assert.Equal(200, ElementHeaderCategory.ReadDesignOption(buffer, first.Length));
    }

    [Fact]
    public void ReadDesignOption_AcceptsTheR20RecordClassTag()
    {
        Assert.Equal(300, ElementHeaderCategory.ReadDesignOption(Record(10, 300, 2, recordClass: 0x04F8), 0));
    }

    [Fact]
    public void ReadDesignOption_IsNullWhenTheOptionFieldIsNotInsideTheDeclaredRecord()
    {
        // the category fits but family, owner view and option do not: the category is read, the option is unknown
        var cut = Record(11, 5, tail: 0)[..^8];
        BitConverter.GetBytes((uint)(cut.Length - 12)).CopyTo(cut, 4);
        Assert.NotNull(ElementHeaderCategory.Read(cut, 0));
        Assert.Null(ElementHeaderCategory.ReadDesignOption(cut, 0));
    }

    [Fact]
    public void ReadDesignOption_IsNullForAnUnreadableHeader()
    {
        Assert.Null(ElementHeaderCategory.ReadDesignOption(Record(12, 5, recordClass: 0x0548), 0));
        Assert.Null(ElementHeaderCategory.ReadDesignOption(Record(13, 5)[..10], 0));
        Assert.Null(ElementHeaderCategory.ReadDesignOption([], 0));
    }

    [Fact]
    public void Record_StoresTheOptionAndAConflictMakesItUnknownForGood()
    {
        var state = new ParsingState();
        ElementHeaderCategory.Record(state, Record(20, 7), 0, 20);
        ElementHeaderCategory.Record(state, Record(20, 7, 2), 0, 20);
        Assert.Equal(7, state.HeaderDesignOptions[20]);
        ElementHeaderCategory.Record(state, Record(20, 8), 0, 20);
        Assert.Null(state.HeaderDesignOptions[20]);
        ElementHeaderCategory.Record(state, Record(20, 7), 0, 20);     // a later agreeing record does not revive it
        Assert.Null(state.HeaderDesignOptions[20]);
    }
}
