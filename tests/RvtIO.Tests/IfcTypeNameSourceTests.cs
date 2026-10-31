using System.Text.Json;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class IfcTypeNameSourceTests
{
    private static ParsingState State() => new()
    {
        ElementNameTable = new() { [500] = "Own type", [700] = "Own family", [800] = "Decoy" },
        NameEntryParentHolder = new() { [500] = 600 },
        NameHolderTarget = new() { [600] = 700 },
    };

    [Fact]
    public void UsesOwnNameAndOwnParent_NotCanonicalTypeOfTheType()
    {
        var element = new Element { Id = 500, TypeId = 800, Name = "Legacy name", FamilyName = "Legacy family" };
        ElementBuilder.ApplyValidatedIfcTypeObjectNames([element], State());
        Assert.Equal("Own family:Own type", element.ValidatedIfcTypeObjectName);
        Assert.Equal("Legacy name", element.Name);
        Assert.Equal("Legacy family", element.FamilyName);
        Assert.DoesNotContain("ValidatedIfcTypeObjectName", JsonSerializer.Serialize(element));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("holder")]
    [InlineData("target")]
    [InlineData("family")]
    [InlineData("blank-name")]
    [InlineData("blank-family")]
    [InlineData("self-family")]
    [InlineData("nonpositive-id")]
    public void IncompleteOwnSourceDoesNotBorrowOrKeepStaleValidation(string mutation)
    {
        var state = State();
        var element = new Element { Id = 500, TypeId = 800, ValidatedIfcTypeObjectName = "stale" };
        switch (mutation)
        {
            case "name": state.ElementNameTable.Remove(500); break;
            case "holder": state.NameEntryParentHolder.Clear(); break;
            case "target": state.NameHolderTarget.Clear(); break;
            case "family": state.ElementNameTable.Remove(700); break;
            case "blank-name": state.ElementNameTable[500] = "  "; break;
            case "blank-family": state.ElementNameTable[700] = "\t"; break;
            case "self-family": state.NameHolderTarget[600] = 500; break;
            case "nonpositive-id": element.Id = -1; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        ElementBuilder.ApplyValidatedIfcTypeObjectNames([element], state);
        Assert.Null(element.ValidatedIfcTypeObjectName);
    }

    [Fact]
    public void IndependentOwnEntriesDoNotLeakToAnOccurrenceOrSystemType()
    {
        var validated = new Element { Id = 500 };
        var occurrence = new Element { Id = 501, TypeId = 500 };
        var systemType = new Element { Id = 800, Name = "System type" };
        ElementBuilder.ApplyValidatedIfcTypeObjectNames([validated, occurrence, systemType], State());
        Assert.Equal("Own family:Own type", validated.ValidatedIfcTypeObjectName);
        Assert.Null(occurrence.ValidatedIfcTypeObjectName);
        Assert.Null(systemType.ValidatedIfcTypeObjectName);
    }

    [Fact]
    public void PostProcessPreservesAnExportOnlyNameFromOwnTableChain()
    {
        var info = new RevitFileInfo();
        var element = new Element { Id = 500, TypeId = 800, Name = "Legacy name" };
        info.Elements.Add(element);
        ElementBuilder.PostProcess(info, State());
        Assert.Equal("Own family:Own type", element.ValidatedIfcTypeObjectName);
    }
}
