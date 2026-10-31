using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The current profile ReferenceCalculator: the Reference is the type's own name when there is no Reference parameter; the type
/// name is also the second part of the "Family:Type:Id" product name.</summary>
public class IfcTypeNameReferenceCurrentTests
{
    private static readonly Element Plain = new() { Id = 10234747 };

    [Theory]
    [InlineData("IFCFLOWFITTING")]
    [InlineData("IFCFLOWCONTROLLER")]
    [InlineData("IFCFLOWTERMINAL")]
    [InlineData("IFCBUILDINGELEMENTPROXY")]
    [InlineData("IFCBEAM")]              // a beam may read another name of the instance
    public void TheTypePartOfTheProductNameIsTheReference(string ifcType)
    {
        Assert.Equal("Фланец DIN 2632 PN16",
            IfcExporter.ResolveTypeNameReferenceCurrent(Plain, ifcType, "(Общ)_DIN_Фланец приварной:Фланец DIN 2632 PN16", null));
    }

    [Fact]
    public void AColonInTheTypeNameStaysInTheReference()
    {
        Assert.Equal("Type:Variant", IfcExporter.ResolveTypeNameReferenceCurrent(Plain, "IFCFLOWFITTING", "Family:Type:Variant", null));
    }

    [Theory]
    [InlineData("IFCFLOWSEGMENT")]       // pipes and ducts take their own curve type name
    [InlineData("IFCWALL")]
    [InlineData("IFCSLAB")]
    public void OtherClassesKeepTheirOwnRules(string ifcType)
    {
        Assert.Null(IfcExporter.ResolveTypeNameReferenceCurrent(Plain, ifcType, "Family:Type", null));
    }

    [Fact]
    public void AValidatedOrBlockedTypeNameOrAReferenceParameterIsNotReplaced()
    {
        var validated = new Element { Id = 1, ValidatedDistributionTypeReferenceName = "Validated" };
        var blocked = new Element { Id = 2, ValidatedDistributionTypeReferenceIsBlocked = true };
        var validatedProxy = new Element { Id = 3, ValidatedProxyTypeReferenceName = "Validated" };
        var explicitReference = new Element
        {
            Id = 4,
            Parameters = [new Parameter { Id = -1, StorageType = StorageType.String, Value = "Own", Definition = new Definition { Name = "Reference" } }],
        };

        Assert.Null(IfcExporter.ResolveTypeNameReferenceCurrent(validated, "IFCFLOWFITTING", "Family:Type", null));
        Assert.Null(IfcExporter.ResolveTypeNameReferenceCurrent(blocked, "IFCFLOWFITTING", "Family:Type", null));
        Assert.Null(IfcExporter.ResolveTypeNameReferenceCurrent(validatedProxy, "IFCBUILDINGELEMENTPROXY", "Family:Type", null));
        Assert.Null(IfcExporter.ResolveTypeNameReferenceCurrent(explicitReference, "IFCFLOWFITTING", "Family:Type", null));
    }

    [Theory]
    [InlineData("Family")]            // no type part
    [InlineData("Family:")]           // empty type part
    [InlineData("")]
    public void ANameWithoutATypePartHasNoReference(string name)
    {
        Assert.Null(IfcExporter.ResolveTypeNameReferenceCurrent(Plain, "IFCFLOWFITTING", name, null));
    }
}
