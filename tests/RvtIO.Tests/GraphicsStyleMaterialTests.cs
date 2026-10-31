using RvtIO.Lookup;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>GStyleElem (class 2050) blocks: the material of the deferred GStyle (GStyle.m_materialElemId), which the expected export
/// takes for a solid whose faces carry none, and the category of the style,
/// whose name orders the solids of a material list. CategoryElem (class 632)
/// blocks: the name of the category.</summary>
public class GraphicsStyleMaterialTests
{
    // Exact GStyleElem blocks of Test_2022.rvt: the graphics style 198354 of the family subcategory 198353 (Casement),
    // and 36, the built-in style of a window category (m_categoryId -2000016).
    private const string FamilyStyleBlock =
        "0gYDAMUpMyhsAAAAAggAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEAAADSBgMA/////////////////////////////////P///wAAAP/////+B9EGAwD/////AQAAAAEAAAAAAAAANjnS/9sGAwAAbAAAAA==";
    private const string BuiltInStyleBlock =
        "JAAAALEzxEqAAAAAAggAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/////40CAQAAACQAAAD/////////////////////////////////////AAAA//////4HcHvh//////8BAAAAAQAAAP/////wAQEAAAAAAAAANjnS/xoAAAAAAAAAAAAAAACAAAAA";

    [Theory]
    [InlineData(FamilyStyleBlock, 198354u, 198363, 198353)]
    [InlineData(BuiltInStyleBlock, 36u, 26, -2000016)]   // the glass material that the faces of the solids of this style carry
    public void MaterialAndCategory_AreReadFromTheOwnGraphicsStyle(
        string base64, uint owner, int expectedMaterial, int expectedCategory)
    {
        byte[] block = Convert.FromBase64String(base64);
        uint dataSize = BitConverter.ToUInt32(block, 8);
        Assert.Equal(owner, BitConverter.ToUInt32(block, 0));
        Assert.True(TfBlockDeserializer.TryExtractGraphicsStyle(block, 0, dataSize, 2050, out int category, out int material));
        Assert.Equal(expectedMaterial, material);
        Assert.Equal(expectedCategory, category);
    }

    [Fact]
    public void GraphicsStyle_IsNotReadFromAWrongClassOrABrokenFooter()
    {
        byte[] block = Convert.FromBase64String(FamilyStyleBlock);
        uint dataSize = BitConverter.ToUInt32(block, 8);
        Assert.False(TfBlockDeserializer.TryExtractGraphicsStyle(block, 0, dataSize, 2046, out _, out _));
        byte[] footer = (byte[])block.Clone();
        footer[^1] ^= 0xFF;
        Assert.False(TfBlockDeserializer.TryExtractGraphicsStyle(footer, 0, dataSize, 2050, out _, out _));
        Assert.False(TfBlockDeserializer.TryExtractGraphicsStyle(block[..^8], 0, dataSize, 2050, out _, out _));
    }

    // Exact CategoryElem blocks of Test_2022.rvt: two subcategories of a window family, a name with a slash, and 235,
    // one of the thousands of categories whose name is empty.
    private const string CasementBlock =
        "0QYDAGgt+pt/AAAAeAIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEAAADRBgMA/////////////////////////////////////wAAAP//////////dQICAAAA0gYDANMGAwAIAAAAQwBhAHMAZQBtAGUAbgB0AHJ74f8BAAAAAAAAAH8AAAA=";
    private const string CillExternalBlock =
        "1AYDAC0vEWSJAAAAeAIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEAAADUBgMA/////////////////////////////////////wAAAP//////////dQICAAAA1QYDANYGAwANAAAAQwBpAGwAbAAgAEUAeAB0AGUAcgBuAGEAbABye+H/AQAAAAAAAACJAAAA";
    private const string MouldingBlock =
        "3BoAAH4715ipAAAAeAIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/////40CAQAAANwaAAD/////////////////////////////////////AAAA//////////91AgIAAADdGgAA3hoAAAEAAAD/////8AETAAAATQBvAHUAbABkAGkAbgBnAC8AQQByAGMAaABpAHQAcgBhAHYAZQBpe+H/AQAAAAAAAAAAAAAAAAAAAKkAAAA=";
    private const string EmptyNameBlock =
        "6wAAALYvfap/AAAAeAIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/////40CAQAAAOsAAAD////////////////////////////////8////AAAA6gAAAP////91AgEAAADsAAAAAQAAAP/////wAQAAAABFe+H/BAAAAAcAAAAAAAAAAAAAAH8AAAA=";

    [Theory]
    [InlineData(CasementBlock, 198353u, "Casement")]
    [InlineData(CillExternalBlock, 198356u, "Cill External")]
    [InlineData(MouldingBlock, 6876u, "Moulding/Architrave")]
    [InlineData(EmptyNameBlock, 235u, "")]
    public void CategoryName_IsReadFromTheDeferredCategory(string base64, uint owner, string expectedName)
    {
        byte[] block = Convert.FromBase64String(base64);
        uint dataSize = BitConverter.ToUInt32(block, 8);
        Assert.Equal(owner, BitConverter.ToUInt32(block, 0));
        Assert.Equal(expectedName, TfBlockDeserializer.ExtractCategoryName(block, 0, dataSize, 632));
    }

    [Fact]
    public void CategoryName_IsNotReadFromAWrongClassOrABrokenFooter()
    {
        byte[] block = Convert.FromBase64String(CasementBlock);
        uint dataSize = BitConverter.ToUInt32(block, 8);
        Assert.Null(TfBlockDeserializer.ExtractCategoryName(block, 0, dataSize, 2050));
        byte[] footer = (byte[])block.Clone();
        footer[^1] ^= 0xFF;
        Assert.Null(TfBlockDeserializer.ExtractCategoryName(footer, 0, dataSize, 632));
        Assert.Null(TfBlockDeserializer.ExtractCategoryName(block[..^8], 0, dataSize, 632));
    }

    [Theory]
    [InlineData(-2000018, "Frame/Mullion")]   // the window frame subcategory: Test_2022 lists its frame before its glass
    [InlineData(-2000016, "Glass")]
    [InlineData(-2000025, "Panel")]
    [InlineData(-2009516, "<Hidden Lines>")]   // the glass of a cabinet door in sample project S
    [InlineData(-2000015, null)]               // the cut category of the window glass has no name
    [InlineData(-1, null)]
    public void BuiltInCategory_IsNamedFromTheEnglishTable(int categoryId, string? expectedName) =>
        Assert.Equal(expectedName, EnglishCategoryNames.Get(categoryId));
}
