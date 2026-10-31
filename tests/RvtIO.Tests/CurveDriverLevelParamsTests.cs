using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class CurveDriverLevelParamsTests
{
    private static Element Level(int id, double elevation)
    {
        var level = new Element { Id = id, IsNativeLevel = true };
        level.Parameters.Add(BipHelpers.MakeBipParam(-1007102, "LEVEL_ELEV", StorageType.Double, elevation));
        return level;
    }

    private static object? Value(Element e, int id) => e.Parameters.FirstOrDefault(p => p.Id == id)?.Value;

    [Fact]
    public void StoredReferenceLevel_GivesElevationAndEndOffsets()
    {
        var beam = new Element { Id = 10, CurveDriverRefLevelId = 1, CurveDriverLineZ = (25.0, 19.0) };
        var info = new RevitFileInfo { Elements = [Level(1, 19.5), beam] };

        ElementBuilder.ApplyCurveDriverLevelParams(info);

        Assert.Equal(1, Value(beam, -1001383));
        Assert.Equal(19.5, Value(beam, -1001653));
        Assert.Equal(5.5, Value(beam, -1001571));
        Assert.Equal(-0.5, Value(beam, -1001572));
    }

    [Fact]
    public void UnsetDriverLevel_UsesTheLevelOfTheSketchPlane()
    {
        var beam = new Element { Id = 10, CurveDriverRefLevelId = -1, AssocSketchPlaneId = 20 };
        beam.Parameters.Add(BipHelpers.MakeBipParam(-1001383, "INSTANCE_REFERENCE_LEVEL_PARAM", StorageType.ElementId, -1));
        var plane = new Element { Id = 20, SketchPlaneDatumId = 1 };
        var info = new RevitFileInfo { Elements = [Level(1, 3.0), plane, beam] };

        ElementBuilder.ApplyCurveDriverLevelParams(info);

        Assert.Equal(1, Value(beam, -1001383));
        Assert.Equal(3.0, Value(beam, -1001653));
        Assert.Null(Value(beam, -1001571)); // no drive line
    }

    [Fact]
    public void SketchPlaneOnNonLevelDatum_LeavesReferenceLevelUnknown()
    {
        var beam = new Element { Id = 10, CurveDriverRefLevelId = -1, AssocSketchPlaneId = 20, CurveDriverLineZ = (1, 2) };
        beam.Parameters.Add(BipHelpers.MakeBipParam(-1001383, "INSTANCE_REFERENCE_LEVEL_PARAM", StorageType.ElementId, -1));
        var plane = new Element { Id = 20, SketchPlaneDatumId = 30 };
        var grid = new Element { Id = 30 };
        var info = new RevitFileInfo { Elements = [plane, grid, beam] };

        ElementBuilder.ApplyCurveDriverLevelParams(info);

        Assert.DoesNotContain(beam.Parameters, p => p.Id is -1001383 or -1001653 or -1001571 or -1001572);
    }

    [Fact]
    public void ElementsWithoutCurveDriver_AreUntouched()
    {
        var wall = new Element { Id = 10 };
        var info = new RevitFileInfo { Elements = [Level(1, 0), wall] };

        ElementBuilder.ApplyCurveDriverLevelParams(info);

        Assert.Empty(wall.Parameters);
    }
}
