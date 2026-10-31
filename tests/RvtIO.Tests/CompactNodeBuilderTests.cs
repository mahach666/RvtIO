using RvtIO.Compact;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

public class CompactNodeBuilderTests
{
    // Class ids of r22_schemas.json: 3524 SWall, 512 BasicWallType, 1794 FamilyInstance, 2558 NestedFamSymRefElem, 2936 PathRein.
    private static Element Wall(ushort? classId = 3524) => new()
    {
        Id = 243274,
        UniqueId = "e3e052f9-0156-11d5-9301-0000863f27ad-0003b64a",
        CategoryId = -2000011, NativeHeaderCategoryId = -2000011,
        FamilyName = "Basic Wall",
        SerializedOwnElementClassId = classId,
    };

    [Fact]
    public void Identity_IsIdClassUniqueIdCategoryLabelAndFamilyName()
    {
        var wallType = new Element { Id = 512, SerializedOwnElementClassId = 512, CategoryId = -2000011, NativeHeaderCategoryId = -2000011 };
        var wall = Wall();
        wall.TypeId = 512;
        var node = new CompactNodeBuilder(new CompactIdentity([wallType, wall])).Build(wall, []);
        Assert.Equal(243274, node.ElementId);
        Assert.Equal("SWall", node.ClassName);
        Assert.Equal("e3e052f9-0156-11d5-9301-0000863f27ad-0003b64a", node.UniqueId);
        Assert.Equal("Walls", node.CategoryLabel);
        Assert.Equal("Basic Wall", node.FamilyName);
        Assert.Empty(node.Properties);
    }

    [Fact]
    public void Pseudo_IsLeftOutAndCounted_WhenTheReaderCannotDecideIt()
    {
        var builder = new CompactNodeBuilder();   // no type element to ask
        var node = builder.Build(Wall(), []);
        Assert.Null(node.CategoryLabel);
        Assert.Null(node.FamilyName);
        Assert.Equal(1, builder.UnknownPseudoCount);
        Assert.Contains("not found", Assert.Single(builder.UnknownPseudo).Key);
    }

    [Theory]
    [InlineData((ushort)3524, "SWall")]
    [InlineData((ushort)512, "BasicWallType")]
    [InlineData((ushort)1794, "FamilyInstance")]
    public void ClassName_IsTheSchemaNameOfTheRootClass(ushort classId, string expected)
    {
        var builder = new CompactNodeBuilder();
        Assert.Equal(expected, builder.Build(Wall(classId), []).ClassName);
        Assert.Equal(0, builder.UnresolvedClassNameCount);
    }

    [Fact]
    public void MissingRootClass_GivesAnEmptyNameAndIsCounted()
    {
        var builder = new CompactNodeBuilder();
        Assert.Equal("", builder.Build(Wall(classId: null), []).ClassName);
        Assert.Equal("", builder.Build(Wall(classId: null), []).ClassName);
        Assert.Equal(2, builder.UnresolvedClassNameCount);
        Assert.Equal(2, builder.UnresolvedClassNames["no root class id"]);
    }

    [Fact]
    public void ClassWithoutASchemaName_GivesAnEmptyNameAndIsCounted()
    {
        var builder = new CompactNodeBuilder();
        Assert.Equal("", builder.Build(Wall(ushort.MaxValue), []).ClassName);
        Assert.Equal(1, builder.UnresolvedClassNames[$"class id {ushort.MaxValue} has no schema name"]);
    }

    [Theory]
    [InlineData((ushort)2558, "NestedFamilyTypeReference")]
    [InlineData((ushort)2936, "PathReinforcement")]
    [InlineData((ushort)1154, "DisplacementPath")]
    public void ClassesReferenceSpellsDifferently_TakeTheReferenceValidatedName(ushort classId, string referenceName)
    {
        var builder = new CompactNodeBuilder();
        Assert.Equal(referenceName, builder.Build(Wall(classId), []).ClassName);
        Assert.Empty(builder.UnresolvedClassNames);
    }

    [Fact]
    public void CategoryLabelIsNull_WhenTheCategoryIsMissingOrHasNoName()
    {
        var withoutCategory = Wall();
        withoutCategory.CategoryId = null; withoutCategory.NativeHeaderCategoryId = null;
        var unnamedCategory = Wall();
        unnamedCategory.CategoryId = 123456789; unnamedCategory.NativeHeaderCategoryId = 123456789;
        var builder = new CompactNodeBuilder();
        Assert.Null(builder.Build(withoutCategory, []).CategoryLabel);
        Assert.Null(builder.Build(unnamedCategory, []).CategoryLabel);
    }

    [Fact]
    public void MissingUniqueId_IsWrittenEmptyAndCounted()
    {
        var element = Wall();
        element.UniqueId = null;
        var builder = new CompactNodeBuilder();
        Assert.Equal("", builder.Build(element, []).UniqueId);
        Assert.Equal(1, builder.MissingUniqueIdCount);
    }

    [Fact]
    public void Properties_AreCarriedAsGiven()
    {
        CompactProperty[] properties = [new("G", "L", "V")];
        Assert.Same(properties, new CompactNodeBuilder().Build(Wall(), properties).Properties);
    }
}
