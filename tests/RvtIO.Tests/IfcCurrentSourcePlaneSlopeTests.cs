using System.Text.Json;
using RvtIO.Ifc;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class IfcCurrentSourcePlaneSlopeTests
{
    [SkippableTheory]
    [InlineData(6957681, 57.52880770915155)]
    [InlineData(6957682, 57.52880770915155)]
    [InlineData(6957698, 90.0)]
    [InlineData(6957699, 57.528807709151515)]
    public void SampleSupportSlopeUsesItsSourcePlaneNormal(int id, double expected)
    {
        using var stream = TestFiles.OpenBlock($"sample-a-support-{id}-grep.bin");
        using var memory = new MemoryStream(); stream.CopyTo(memory); byte[] bytes = memory.ToArray();
        var geometry = ElementBuilder.BuildGeometryData(GRepParserV2.Parse(bytes, 0, BitConverter.ToUInt32(bytes, 8))!)!;
        Assert.All(geometry.Faces, face => { Assert.Equal(3, face.PlaneXAxis!.Length); Assert.Equal(3, face.PlaneYAxis!.Length); });
        string before = JsonSerializer.Serialize(geometry);
        Assert.InRange(Math.Abs(expected - IfcExporter.LargestUpwardFaceSlopeCurrent(geometry)!.Value), 0, 1e-12);
        Assert.Equal(before, JsonSerializer.Serialize(geometry));
    }

    [Theory]
    [InlineData(false, 60.0)]
    [InlineData(true, 90.0)]
    public void StrictPositiveZUsesCompleteSourceFrameWithoutSnapping(bool incomplete, double expected)
    {
        var side = new GeoFace { Area = 20, Normal = [0, 1, 1e-15],
            PlaneXAxis = [1, 0, 0], PlaneYAxis = incomplete ? null : [0, 0, 1] };
        var top = new GeoFace { Area = 1, Normal = [-Math.Sqrt(3) / 2, 0, 0.5] };
        var geometry = new GeometryData { Faces = [side, top], BrepSolids = [new GeoBrepSolid { FaceIndices = [0, 1] }] };
        Assert.Equal(expected, IfcExporter.LargestUpwardFaceSlopeCurrent(geometry)!.Value, 12);
    }
}
