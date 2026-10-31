using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

public class ProvisionForVoidTests
{
    private static GeometryData Box(double x, double y, double z)
    {
        double[][] p =
        [
            [0, 0, 0], [x, 0, 0], [x, y, 0], [0, y, 0],
            [0, 0, z], [x, 0, z], [x, y, z], [0, y, z],
        ];
        int[][] faces = [[0, 1, 2, 3], [4, 5, 6, 7], [0, 1, 5, 4], [2, 3, 7, 6], [0, 3, 7, 4], [1, 2, 6, 5]];
        return new GeometryData
        {
            Faces = faces.Select(f => new GeoFace { OuterVertices = f.Select(i => p[i]).ToList() }).ToList(),
            BrepSolids = [new GeoBrepSolid { IsClosed = true, FaceIndices = [0, 1, 2, 3, 4, 5] }],
        };
    }

    private static (Element Proxy, Dictionary<int, Element> Elements) Scene(GeometryData symbolGeometry)
    {
        var proxy = new Element
        {
            Id = 1, Geometry = new GeometryData { ExactMappedInstances = [new GeoInstance { SymbolId = 2 }] },
        };
        return (proxy, new Dictionary<int, Element> { [1] = proxy, [2] = new Element { Id = 2, Geometry = symbolGeometry } });
    }

    [Fact]
    public void BoxSymbol_GivesLongSideWidthAndShortSideHeightAndDepth()
    {
        var (proxy, elements) = Scene(Box(0.3937007874015748, 6.364829396325458, 0.459317585301837));
        var (width, height, depth) = IfcExporter.ResolveProvisionForVoidDimensions(proxy, elements)!.Value;
        Assert.Equal(1940, width, 6);
        Assert.Equal(120, height, 6);
        Assert.Equal(height, depth);
    }

    [Fact]
    public void SymbolThatIsNoBox_HasNoDimensions()
    {
        var geometry = Box(1, 2, 3);
        geometry.Faces[0].OuterVertices![0] = [0.3, 0.2, 0.1]; // a corner moved off its plane
        var (proxy, elements) = Scene(geometry);
        Assert.Null(IfcExporter.ResolveProvisionForVoidDimensions(proxy, elements));
    }

    [Fact]
    public void ProxyWithoutOneMappedSymbol_HasNoDimensions()
    {
        var (proxy, elements) = Scene(Box(1, 2, 3));
        proxy.Geometry!.ExactMappedInstances = null;
        Assert.Null(IfcExporter.ResolveProvisionForVoidDimensions(proxy, elements));
    }
}
