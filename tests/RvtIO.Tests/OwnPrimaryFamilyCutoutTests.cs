using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

public class OwnPrimaryFamilyCutoutTests
{
    [Fact]
    public void UnfilledPrimaryKeepsTheSourceLoopPlaneAndFullCutRange()
    {
        var (insert, wall) = Inputs();
        wall.OwnWallCutoutSteps = [Step(4137, 9, insert.Id)];
        Assert.True(IfcExporter.TryBuildOwnUnfilledPrimaryFamilyCutout(insert, wall, out var opening));
        Assert.Equal(9.5, opening.CentreXFt, 12);
        Assert.Equal(21, opening.CentreYFt, 12);
        Assert.Equal(1.6, opening.DepthFt, 12);
        Assert.Equal(wall.Id, opening.OwnPrimaryFamilyHostId);
        Assert.True(IfcExporter.TryBuildOwnPrimaryFamilyCutout(insert, wall, out var filled));
        Assert.Equal(10, filled.CentreXFt, 12);
        Assert.Equal(0.4, filled.DepthFt, 12);
    }

    [Theory]
    [InlineData("missing-step")] [InlineData("duplicate-step")] [InlineData("shadow-step")]
    [InlineData("wrong-instance")] [InlineData("missing-source")]
    public void UnfilledPrimaryRequiresTheUniqueOwnOriginalOperation(string mutation)
    {
        var (insert, wall) = Inputs();
        var step = Step(4137, 9, insert.Id);
        wall.OwnWallCutoutSteps = [step];
        switch (mutation)
        {
            case "missing-step": wall.OwnWallCutoutSteps = null; break;
            case "duplicate-step": wall.OwnWallCutoutSteps = [step, step]; break;
            case "shadow-step": wall.OwnWallCutoutSteps = [step with { ClassId = 4155 }]; break;
            case "wrong-instance": wall.OwnWallCutoutSteps = [step with { InstanceId = 999 }]; break;
            case "missing-source": insert.OwnSingleFamilyCutout = null; break;
        }
        Assert.False(IfcExporter.TryBuildOwnUnfilledPrimaryFamilyCutout(insert, wall, out _));
    }

    [Theory]
    [InlineData(true, 0, false, 1.6)]
    [InlineData(true, 99, true, 3)]
    [InlineData(true, 99, false, 3)]
    [InlineData(true, 0, true, 3)]
    [InlineData(false, 0, false, 3)]
    public void FullPrimarySolidOnlyReplacesAnUnemittedFillingInCurrent(bool currentProfile, int filling,
        bool createFill, double extrusionDepthFeet)
    {
        var (insert, wall) = Inputs();
        wall.OwnWallCutoutSteps = [Step(4137, 9, insert.Id)];
        Assert.True(IfcExporter.TryBuildOwnPrimaryFamilyCutout(insert, wall, out var primary));
        var writer = new StepWriter();
        var exporter = new IfcExporter { Profile = currentProfile ? IfcProfile.Current : IfcProfile.Legacy };
        var method = typeof(IfcExporter).GetMethod("CreateHostedOpeningAbsolute",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        method.Invoke(exporter, [writer, insert, filling, wall.BoundingBox!, wall, 1, 2, 3, 4,
            new List<int>(), new Dictionary<int, Element> { [wall.Id] = wall, [insert.Id] = insert },
            createFill, new Dictionary<(string, string), int>(), primary, false, false]);
        var entities = Enumerable.Range(0, writer.EntityCount).Select(writer.EntityAt).ToArray();
        var product = Assert.Single(entities, entity => entity.Type == "IFCPRODUCTDEFINITIONSHAPE");
        var index = Enumerable.Range(0, writer.EntityCount).ToDictionary(i => writer.EntityAt(i).Id);
        var extrusion = IfcExporter.ReadBodyExtrusionCurrent(writer, index, $"#{product.Id}")!.Value;
        Assert.Equal(extrusionDepthFeet * 0.3048, extrusion.Depth, 10);
        Assert.Equal(createFill ? 1 : 0, entities.Count(entity => entity.Type == "IFCRELFILLSELEMENT"));
    }

    [Theory]
    [InlineData(false, 1)] [InlineData(true, 1)] [InlineData(false, -1)] [InlineData(true, -1)]
    public void ExtraHostUsesThePrimarySourcePlaneAndEffectiveWallWidth(bool shadow, int normalSign)
    {
        var (insert, primary, extra, elements) = ExtraInputs(shadow);
        primary.OwnWallSide0NormalCurrent = [normalSign, 0, 0];
        Assert.True(IfcExporter.TryBuildOwnExtraHostFamilyCutout(insert, extra, elements, out var opening));
        Assert.Equal(9.5 - normalSign * 0.3, opening.CentreXFt, 12);
        Assert.Equal(21, opening.CentreYFt, 12);
        Assert.Equal(0.6, opening.DepthFt, 12);
        Assert.Equal(0, opening.OwnPrimaryFamilyHostId);
        Assert.False(opening.IsFamilyCutLoop);
        Assert.True(IfcExporter.TryBuildOwnPrimaryFamilyCutout(insert, primary, out var filled));
        Assert.Equal(10, filled.CentreXFt, 12);
        Assert.Equal(0.4, filled.DepthFt, 12);
    }

    [Theory]
    [InlineData("no-normal")] [InlineData("nonfinite-normal")] [InlineData("nonunit-normal")]
    [InlineData("nonperpendicular-normal")] [InlineData("sloping-normal")]
    [InlineData("no-membership")] [InlineData("no-width")] [InlineData("negative-width")]
    [InlineData("nonfinite-width")] [InlineData("no-primary-validation")]
    [InlineData("duplicate-step")] [InlineData("wrong-class")] [InlineData("wrong-generator")]
    public void ExtraHostRejectsMissingOrAmbiguousSourceValidation(string mutation)
    {
        var (insert, primary, extra, elements) = ExtraInputs(true);
        switch (mutation)
        {
            case "no-normal": primary.OwnWallSide0NormalCurrent = null; break;
            case "nonfinite-normal": primary.OwnWallSide0NormalCurrent = [double.NaN, 0, 0]; break;
            case "nonunit-normal": primary.OwnWallSide0NormalCurrent = [2, 0, 0]; break;
            case "nonperpendicular-normal": primary.OwnWallSide0NormalCurrent = [0, 1, 0]; break;
            case "sloping-normal": primary.OwnWallSide0NormalCurrent = [1, 0, 1e-15]; break;
            case "no-membership": insert.ExtraHostIds = []; break;
            case "no-width": elements[400].ValidatedWallTypeThicknessFeet = null; break;
            case "negative-width": elements[400].ValidatedWallTypeThicknessFeet = -0.6; break;
            case "nonfinite-width": elements[400].ValidatedWallTypeThicknessFeet = double.NaN; break;
            case "no-primary-validation": insert.SerializedHostId = 999; break;
            case "duplicate-step": extra.OwnWallCutoutSteps = [.. extra.OwnWallCutoutSteps!, .. extra.OwnWallCutoutSteps!]; break;
            case "wrong-class": extra.OwnWallCutoutSteps = [extra.OwnWallCutoutSteps![0] with { ClassId = 4130 }]; break;
            case "wrong-generator": extra.OwnWallCutoutSteps = [extra.OwnWallCutoutSteps![0] with { OtherGeneratorId = 999 }]; break;
        }
        Assert.False(IfcExporter.TryBuildOwnExtraHostFamilyCutout(insert, extra, elements, out _));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExtraHostOperandReachesTheCommonBodyWriter(bool shadow)
    {
        var (insert, primary, extra, elements) = ExtraInputs(shadow);
        Assert.True(IfcExporter.TryBuildOwnSecondaryFamilyCutout(insert, extra, elements, out var opening));
        var writer = new StepWriter();
        Assert.True(new IfcExporter().TryCreateOwnFamilyCutoutBodyCurrent(writer, insert, extra,
            elements, opening, 1, out int shape));
        Assert.True(shape > 0);
    }

    private static (Element Insert, Element Primary, Element Extra, Dictionary<int, Element> Elements) ExtraInputs(bool shadow)
    {
        var (insert, primary) = Inputs();
        primary.OwnWallSide0NormalCurrent = [1, 0, 0];
        primary.OwnWallCutoutSteps = [Step(4137, 9, insert.Id)];
        var extra = new Element { Id = 400, ValidatedWallTypeThicknessFeet = 0.6,
            OwnWallCrossSection = 1, OwnWallAxisLine = [[0, 0, 0], [10, 0, 0]],
            OwnWallCutoutSteps = [Step(4137, 10, insert.Id)] };
        insert.ExtraHostIds = [extra.Id];
        var elements = new Dictionary<int, Element> { [primary.Id] = primary, [extra.Id] = extra };
        if (!shadow) return (insert, primary, extra, elements);
        var forwarded = new Element { Id = 500, OwnWallCrossSection = 1,
            OwnWallAxisLine = [[0, 0, 0], [0, 10, 0]],
            OwnWallCutoutSteps = [Step(4155, 11, insert.Id, extra.Id, 10)] };
        elements[forwarded.Id] = forwarded;
        return (insert, primary, forwarded, elements);
    }

    [Theory]
    [InlineData(0, 10, 1.6)] [InlineData(0, -10, 1.6)] [InlineData(10, 0, 2)]
    public void ShadowBodyExtrudesAcrossTheOwnWallAxis(double dx, double dy, double depthFeet)
    {
        var (insert, wall, secondary, elements) = ShadowBodyInputs();
        secondary.OwnWallAxisLine = [[0, 0, 0], [dx, dy, 0]];
        Assert.True(IfcExporter.TryBuildOwnShadowFamilyCutout(insert, secondary, elements, out var opening));
        var writer = new StepWriter();
        Assert.True(new IfcExporter().TryCreateOwnFamilyCutoutBodyCurrent(writer, insert, secondary,
            elements, opening, 1, out int shape));
        var index = Enumerable.Range(0, writer.EntityCount).ToDictionary(i => writer.EntityAt(i).Id);
        var body = IfcExporter.ReadBodyExtrusionCurrent(writer, index, $"#{shape}")!.Value;
        Assert.Equal(depthFeet * 0.3048, body.Depth, 10);
        Assert.Equal(0, body.AxisZ![2], 12);
        Assert.Equal(3 * 0.3048, body.Loop!.Max(point => point[2]) - body.Loop.Min(point => point[2]), 10);
    }

    [Theory]
    [InlineData("no-axis")] [InlineData("nonfinite-axis")] [InlineData("zero-axis")]
    [InlineData("nonvertical-wall")] [InlineData("different-operand")] [InlineData("legacy")]
    public void UnconfirmedShadowBodyKeepsTheExistingWriterWithoutAppendingEntities(string mutation)
    {
        var (insert, wall, secondary, elements) = ShadowBodyInputs();
        Assert.True(IfcExporter.TryBuildOwnShadowFamilyCutout(insert, secondary, elements, out var opening));
        switch (mutation)
        {
            case "no-axis": secondary.OwnWallAxisLine = null; break;
            case "nonfinite-axis": secondary.OwnWallAxisLine![1][0] = double.NaN; break;
            case "zero-axis": secondary.OwnWallAxisLine = [[0, 0, 0], [0, 0, 0]]; break;
            case "nonvertical-wall": secondary.OwnWallCrossSection = 0; break;
            case "different-operand": opening = opening with { DepthFt = 10 }; break;
        }
        var exporter = new IfcExporter { Profile = mutation == "legacy" ? IfcProfile.Legacy : IfcProfile.Current };
        var writer = new StepWriter();
        Assert.False(exporter.TryCreateOwnFamilyCutoutBodyCurrent(writer, insert, secondary,
            elements, opening, 1, out _));
        Assert.Equal(0, writer.EntityCount);
    }

    [Fact]
    public void ObliqueOwnWallAxisWritesBrepWithoutExtrusionQuantities()
    {
        var (insert, wall, secondary, elements) = ShadowBodyInputs();
        secondary.OwnWallAxisLine = [[0, 0, 0], [1, 1, 0]];
        Assert.True(IfcExporter.TryBuildOwnShadowFamilyCutout(insert, secondary, elements, out var opening));
        var writer = new StepWriter();
        Assert.True(new IfcExporter().TryCreateOwnFamilyCutoutBodyCurrent(writer, insert, secondary,
            elements, opening, 1, out int shape));
        var index = Enumerable.Range(0, writer.EntityCount).ToDictionary(i => writer.EntityAt(i).Id);
        Assert.Null(IfcExporter.ReadBodyExtrusionCurrent(writer, index, $"#{shape}"));
        Assert.Contains(Enumerable.Range(0, writer.EntityCount).Select(writer.EntityAt),
            entity => entity.Type == "IFCFACETEDBREP");
    }

    private static (Element Insert, Element Wall, Element Secondary, Dictionary<int, Element> Elements) ShadowBodyInputs()
    {
        var (insert, wall) = Inputs();
        wall.OwnWallCutoutSteps = [Step(4137, 9, insert.Id)];
        var secondary = new Element { Id = 400, OwnWallCrossSection = 1,
            OwnWallAxisLine = [[0, 0, 0], [0, 10, 0]],
            OwnWallCutoutSteps = [Step(4155, 20, insert.Id, wall.Id, 9)] };
        return (insert, wall, secondary, new() { [wall.Id] = wall });
    }

    [Fact]
    public void ShadowUsesOriginalOperationWidthAndLoopPlane()
    {
        var (insert, wall) = Inputs();
        wall.OwnWallCutoutSteps = [Step(4137, 9, insert.Id)];
        var secondary = new Element { Id = 400,
            OwnWallCutoutSteps = [Step(4155, 20, insert.Id, wall.Id, 9)],
            ValidatedWallTypeThicknessFeet = 0.01 };
        Assert.True(IfcExporter.TryBuildOwnShadowFamilyCutout(insert, secondary,
            new Dictionary<int, Element> { [wall.Id] = wall }, out var opening));
        Assert.Equal(9.5, opening.CentreXFt, 12);
        Assert.Equal(21, opening.CentreYFt, 12);
        Assert.Equal(1.6, opening.DepthFt, 12);
        Assert.Equal(0, opening.OwnPrimaryFamilyHostId);
        Assert.False(opening.IsFamilyCutLoop);
    }

    [Theory]
    [InlineData("no-shadow")] [InlineData("wrong-generator")] [InlineData("wrong-wall")]
    [InlineData("wrong-instance")] [InlineData("duplicate-original")]
    [InlineData("duplicate-shadow")] [InlineData("recursive-shadow")] [InlineData("no-primary-validation")]
    public void ShadowRequiresUniqueDirectOriginalOperation(string mutation)
    {
        var (insert, wall) = Inputs();
        var source = Step(4137, 9, insert.Id);
        var shadow = Step(4155, 20, insert.Id, wall.Id, 9);
        wall.OwnWallCutoutSteps = [source];
        var secondary = new Element { Id = 400, OwnWallCutoutSteps = [shadow] };
        switch (mutation)
        {
            case "no-shadow": secondary.OwnWallCutoutSteps = null; break;
            case "wrong-generator": secondary.OwnWallCutoutSteps = [shadow with { OtherGeneratorId = 99 }]; break;
            case "wrong-wall": secondary.OwnWallCutoutSteps = [shadow with { OtherWallId = 99 }]; break;
            case "wrong-instance": wall.OwnWallCutoutSteps = [source with { InstanceId = 99 }]; break;
            case "duplicate-original": wall.OwnWallCutoutSteps = [source, source]; break;
            case "duplicate-shadow": secondary.OwnWallCutoutSteps = [shadow, shadow]; break;
            case "recursive-shadow": wall.OwnWallCutoutSteps = [source with { ClassId = 4155 }]; break;
            case "no-primary-validation": insert.OwnSingleFamilyCutout = null; break;
        }
        Assert.False(IfcExporter.TryBuildOwnShadowFamilyCutout(insert, secondary,
            new Dictionary<int, Element> { [wall.Id] = wall }, out _));
    }

    private static RvtIO.Parsing.TfBlockDeserializer.OwnWallCutoutStep Step(
        ushort cls, int generator, int instance, int? otherWall = null, int? otherGenerator = null)
        => new(0, 0, cls, 0, 0, 1, instance, -1, -1, 0, 0, 0, null,
            generator, otherWall, otherGenerator);

    [Fact]
    public void FinitePrimaryBoxUsesPairedRowMajorTransformAndOwnAxisPlane_NotNetBody()
    {
        var (insert, wall) = Inputs();
        Assert.True(IfcExporter.TryBuildOwnPrimaryFamilyCutout(insert, wall, out var opening));
        Assert.Equal(10, opening.CentreXFt, 12); Assert.Equal(21, opening.CentreYFt, 12);
        Assert.Equal(2, opening.WidthFt, 12); Assert.Equal(3, opening.HeightFt, 12);
        Assert.Equal(30, opening.BaseZFt, 12); Assert.Equal(0.4, opening.DepthFt, 12);
        Assert.Equal(300, opening.OwnPrimaryFamilyHostId); Assert.True(opening.ExactDepth);
        Assert.False(opening.IsFamilyCutLoop); Assert.Null(opening.Boundary);
        Assert.True(IfcExporter.TryTargetCommonHostedOpeningToHost(opening, wall, out var targeted));
        Assert.Equal(opening, targeted);
        Assert.True(IfcExporter.TryResolveHostedOpeningWriterOperand(insert, [], wall,
            new Dictionary<int, Element>(), opening, out var operand));
        Assert.Equal(opening, operand);
    }

    [Theory]
    [InlineData("no-validation")] [InlineData("wrong-symbol")] [InlineData("wrong-primary-host")]
    [InlineData("no-host-operation")] [InlineData("no-own-axis")] [InlineData("slanted-wall")]
    [InlineData("nonparallel-axis")] [InlineData("no-own-width")] [InlineData("zero-width")]
    [InlineData("nonfinite-width")] [InlineData("nonfinite-matrix")]
    [InlineData("generic-model")]
    public void UnconfirmedInputsDoNotChangeTheLegacyOrSecondaryRoute(string mutation)
    {
        var (insert, wall) = Inputs();
        switch (mutation)
        {
            case "no-validation": insert.OwnSingleFamilyCutout = null; break;
            case "wrong-symbol": insert.ValidatedOriginalFamilySymbolId = 700; break;
            case "wrong-primary-host": insert.SerializedHostId = 400; break;
            case "no-host-operation": wall.OwnWallCutoutInstanceIds = [999]; break;
            case "no-own-axis": wall.OwnWallAxisLine = null; break;
            case "slanted-wall": wall.OwnWallCrossSection = 0; break;
            case "nonparallel-axis": wall.OwnWallAxisLine = [[0, 0, 0], [1, 0, 0]]; break;
            case "no-own-width": wall.ValidatedWallTypeThicknessFeet = null; break;
            case "zero-width": wall.ValidatedWallTypeThicknessFeet = 0; break;
            case "nonfinite-width": wall.ValidatedWallTypeThicknessFeet = double.PositiveInfinity; break;
            case "nonfinite-matrix": insert.OwnSingleFamilyCutout!.Value.Transform[2] = double.NaN; break;
            case "generic-model": insert.CategoryId = -2000151; break;
        }
        Assert.False(IfcExporter.TryBuildOwnPrimaryFamilyCutout(insert, wall, out _));
    }

    [Fact]
    public void SourcePrimaryOperandCannotBeRetargetedToASecondaryWall()
    {
        var (insert, wall) = Inputs();
        Assert.True(IfcExporter.TryBuildOwnPrimaryFamilyCutout(insert, wall, out var opening));
        var secondary = new Element { Id = 400 };
        Assert.False(IfcExporter.TryTargetCommonHostedOpeningToHost(opening, secondary, out _));
        Assert.False(IfcExporter.TryResolveHostedOpeningWriterOperand(insert, [], secondary,
            new Dictionary<int, Element>(), opening, out _));
        Assert.False(IfcExporter.TryBuildOwnPrimaryFamilyCutout(insert, secondary, out _));
    }

    private static (Element Insert, Element Wall) Inputs()
    {
        var insert = new Element { Id = 100, CategoryId = -2000023,
            SerializedHostId = 300, ValidatedOriginalFamilySymbolId = 600 };
        insert.OwnSingleFamilyCutout = (600, 300,
            [0, -1, 0, 1, 0, 0, 0, 0, 1, 10, 20, 30],
            new([[0, 0.5, 0], [2, 0.5, 0], [2, 0.5, 3], [0, 0.5, 3]], [0, -1, 0], 5));
        var wall = new Element { Id = 300, OwnWallCrossSection = 1, OwnWallCutoutInstanceIds = [100],
            OwnWallAxisLine = [[10, 0, 0], [10, 50, 0]], ValidatedWallTypeThicknessFeet = 0.4,
            // The already cut net body, synthetic parameters and public axis are not source authority.
            BoundingBox = [999, 999, 999, 1000, 1000, 1000], Geometry = new(),
            Location = new() { StartPoint = [0, 0, 0], EndPoint = [50, 0, 0] } };
        return (insert, wall);
    }
}
