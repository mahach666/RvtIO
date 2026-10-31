using System.Reflection;
using RvtIO.Ifc;
using Xunit;

namespace RvtIO.Tests;

/// <summary>Categories whose elements are parts of a railing, not products of their own: the current profile exports none of the
/// handrails of a sample architectural model.</summary>
public class IfcNonPhysicalCategoryTests
{
    [Theory]
    [InlineData("OST_RailingHandRail", true)]
    [InlineData("OST_RailingTopRail", true)]
    [InlineData("OST_StairsRailing", false)]
    [InlineData("OST_Walls", false)]
    public void RailingParts_AreNotProducts(string category, bool expected)
    {
        var method = typeof(IfcExporter).GetMethod("IsNonPhysicalCategory", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal(expected, method.Invoke(null, [category]));
    }
}
