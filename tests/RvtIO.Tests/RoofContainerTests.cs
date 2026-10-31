using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

public class RoofContainerTests
{
    private static Element Roof(int id) => new() { Id = id, CategoryId = -2000035 };
    private static Element Panel(int id, int hostId, int category = -2000170) =>
        new() { Id = id, CategoryId = category, SerializedHostId = hostId };

    private static (Dictionary<int, Element> ById, Dictionary<int, int> Counts) Index(params Element[] elements) =>
        (elements.ToDictionary(e => e.Id), elements.ToDictionary(e => e.Id, _ => 1));

    [Fact]
    public void PanelsAndMullionsOfAGeometrylessRoof_AreItsChildren()
    {
        var elements = new[] { Roof(1), Panel(2, 1), Panel(3, 1, -2000171) };
        var (byId, counts) = Index(elements);
        var result = IfcExporter.ResolveRoofContainerChildren(elements, byId, counts);
        Assert.Equal([2, 3], result[1]);
    }

    [Fact]
    public void RoofWithABody_IsNoContainer()
    {
        var roof = Roof(1);
        roof.BoundingBox = [0, 0, 0, 1, 1, 1];
        var elements = new[] { roof, Panel(2, 1) };
        var (byId, counts) = Index(elements);
        Assert.Empty(IfcExporter.ResolveRoofContainerChildren(elements, byId, counts));
    }

    [Fact]
    public void NonCurtainChildrenOrNonRoofHosts_AreIgnored()
    {
        var wall = new Element { Id = 1, CategoryId = -2000011 };
        var elements = new[] { wall, Panel(2, 1), Roof(3), new Element { Id = 4, CategoryId = -2000023, SerializedHostId = 3 } };
        var (byId, counts) = Index(elements);
        Assert.Empty(IfcExporter.ResolveRoofContainerChildren(elements, byId, counts));
    }
}
