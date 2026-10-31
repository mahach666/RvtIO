using RvtIO.Internal;
using RvtIO.Lookup;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class MepCurveParamsTests
{
    private static Dictionary<int, object> Geometry(ushort curveClass, TfBlockDeserializer.NativeParams np)
    {
        var applied = new Dictionary<int, object>();
        ElementBuilder.ApplyMepCurveGeometry(np, curveClass, (id, storage, value, force) =>
        {
            Assert.Equal(StorageType.Double, storage);
            Assert.True(force);
            applied[id] = value;
        });
        return applied;
    }

    private static TfBlockDeserializer.NativeParams Line(double[] origin, double[] direction, double endParam0, double endParam1,
        double offsetStart, double offsetEnd, double width, double height)
    {
        var np = new TfBlockDeserializer.NativeParams();
        for (int i = 0; i < 3; i++)
        {
            np.DoubleFields["87:m_pCrv.origin@" + i] = origin[i];
            np.DoubleFields["87:m_pCrv.dirVec@" + i] = direction[i];
        }
        np.DoubleFields["87:m_pCrv.endParams@0"] = endParam0;
        np.DoubleFields["87:m_pCrv.endParams@1"] = endParam1;
        np.DoubleFields["602:m_dOffsetStart"] = offsetStart;
        np.DoubleFields["602:m_dOffsetEnd"] = offsetEnd;
        np.DoubleFields["602:m_dWidthOrDiameter"] = width;
        np.DoubleFields["602:m_dHeight"] = height;
        return np;
    }

    [Fact]
    public void HorizontalRectangularDuct_LengthAreaAndElevations()
    {
        var np = Line([0, 10, 5], [0, -1, 0], 0, 4, 10, 10, 1, 2);
        np.DoubleFields["1219:m_dEqDiameter"] = 1.5;

        var applied = Geometry(3148, np);

        Assert.Equal(4.0, applied[-1004005]);
        Assert.Equal(2 * (1 + 2) * 4.0, applied[-1114120]);
        Assert.Equal(11.0, applied[-1140239]);
        Assert.Equal(9.0, applied[-1140240]);
        Assert.Equal(10.0, applied[-1114132]);
        Assert.False(applied.ContainsKey(-1140255) && (double)applied[-1140255] != 0.0);
    }

    [Fact]
    public void RoundDuct_IsRecognisedByItsEquivalentDiameter()
    {
        var np = Line([0, 0, 0], [1, 0, 0], 0, 3, 8, 8, 1, 1);
        np.DoubleFields["1219:m_dEqDiameter"] = 1;

        Assert.Equal(Math.PI * 1 * 3, Geometry(3148, np)[-1114120]);
    }

    [Fact]
    public void SlopedDuct_UsesTheHorizontalComponentOfTheDirection()
    {
        var np = Line([0, 0, 0], [0.6, 0, 0.8], 0, 5, 10, 14, 1, 2);

        var applied = Geometry(3148, np);

        Assert.Equal(5.0, (double)applied[-1004005], 12);
        Assert.Equal(14 + 1 * 0.6, (double)applied[-1140239], 12);
        Assert.Equal(10 - 1 * 0.6, (double)applied[-1140240], 12);
        Assert.Equal(4.0 / 3.0, (double)applied[-1140255], 12);
    }

    [Fact]
    public void VerticalPipe_HasNoSlopeAndTheInvertIsTheLowerEnd()
    {
        var np = Line([3, 4, 20], [0, 0, 1], 1, 6, -2, 3, 0.5, 0.5);
        np.DoubleFields["2964:m_dOuterDiameter"] = 0.6;
        np.DoubleFields["2964:m_dInnerDiameter"] = 0.4;

        var applied = Geometry(3210, np);

        Assert.Equal(5.0, applied[-1004005]);
        Assert.Equal(Math.PI * 0.5 * 5, applied[-1114120]);
        Assert.Equal(3.0, applied[-1155114]);
        Assert.Equal(-2.0, applied[-1155115]);
        Assert.Equal(21.0, applied[-1140237]);
        Assert.False(applied.ContainsKey(-1140256));
    }

    [Fact]
    public void HorizontalPipe_InvertSubtractsTheInnerRadius()
    {
        var np = Line([0, 0, 40], [1, 0, 0], 0, 2, 5, 5, 0.5, 0.5);
        np.DoubleFields["2964:m_dOuterDiameter"] = 0.6;
        np.DoubleFields["2964:m_dInnerDiameter"] = 0.4;

        var applied = Geometry(3210, np);

        Assert.Equal(5.3, (double)applied[-1155114], 12);
        Assert.Equal(4.7, (double)applied[-1155115], 12);
        Assert.Equal(39.8, (double)applied[-1140237], 12);
    }

    [Fact]
    public void ConduitAndCableTray_UseTheirOwnSizes()
    {
        var conduit = Line([0, 0, 0], [1, 0, 0], 0, 2, 1, 1, 0.1, 0.1);
        conduit.DoubleFields["749:m_dOuterDiameter"] = 0.2;
        var tray = Line([0, 0, 0], [1, 0, 0], 0, 2, 1, 1, 0.5, 0.3);

        var conduitParams = Geometry(3134, conduit);
        var trayParams = Geometry(598, tray);

        Assert.Equal(1.1, (double)conduitParams[-1140124], 12);
        Assert.Equal(0.9, (double)conduitParams[-1140125], 12);
        Assert.Equal(1.15, (double)trayParams[-1140124], 12);
        Assert.Equal(0.85, (double)trayParams[-1140125], 12);
    }

    [Fact]
    public void CurveWithoutAStoredLine_AddsNothing()
    {
        Assert.Empty(Geometry(3148, new TfBlockDeserializer.NativeParams()));
    }

    private static object? Value(Element e, int id) => e.Parameters.FirstOrDefault(p => p.Id == id)?.Value;

    private static TfBlockDeserializer.NativeParams Connectors(params (int System, int Section, int Direction)[] connectors)
    {
        var np = new TfBlockDeserializer.NativeParams();
        for (int k = 0; k < connectors.Length; k++)
        {
            np.Fields[$"3556#{k}:m_idSystem"] = connectors[k].System;
            np.Fields[$"3556#{k}:m_nSection"] = connectors[k].Section;
            np.Fields[$"3556#{k}:m_eDirection"] = connectors[k].Direction;
        }
        return np;
    }

    private static (ParsingState State, RevitSystemFamilyNames Names) MepState()
    {
        var state = new ParsingState();
        var system = new TfBlockDeserializer.NativeParams();
        system.StringFields["3170:m_strName"] = "Mechanical Supply Air 1";
        system.Fields["3170:m_typeId"] = 50;
        state.NativeParams[40] = system;
        var type = new TfBlockDeserializer.NativeParams();
        type.Fields["3190:m_basicSystemType"] = 1;
        type.StringFields["3190:m_abbreviation"] = "SA";
        state.NativeParams[50] = type;
        var names = RevitSystemFamilyNames.ForTests(new Dictionary<SystemFamilyKind, string>(),
            texts: new Dictionary<RevitUiText, string>
            {
                [RevitUiText.SupplyAir] = "Supply Air",
                [RevitUiText.SystemUndefined] = "Undefined",
            });
        return (state, names);
    }

    [Fact]
    public void DuctInASystem_TakesNameTypeClassificationAbbreviationAndInletSection()
    {
        var (state, names) = MepState();
        var duct = new Element { Id = 1 };
        state.ElemClassIdx[1] = 3148;
        state.NativeParams[1] = Connectors((40, 17, 2), (40, 19, 1), (40, 23, 2));

        ElementBuilder.ApplyMepSystemParams(new RevitFileInfo { Elements = [duct] }, state, names);

        Assert.Equal("Mechanical Supply Air 1", Value(duct, -1140324));
        Assert.Equal(50, Value(duct, -1140333));
        Assert.Equal("Supply Air", Value(duct, -1140325));
        Assert.Equal("SA", Value(duct, -1150468));
        Assert.Equal(19, Value(duct, -1114125));
    }

    [Fact]
    public void DuctWithoutASystem_IsUndefined_WithoutName()
    {
        var (state, names) = MepState();
        var duct = new Element { Id = 1 };
        duct.Parameters.Add(BipHelpers.MakeBipParam(-1140324, "RBS_SYSTEM_NAME_PARAM", StorageType.String, "stale"));
        state.ElemClassIdx[1] = 3148;
        state.NativeParams[1] = Connectors((-1, 3, 0), (-1, 3, 0));

        ElementBuilder.ApplyMepSystemParams(new RevitFileInfo { Elements = [duct] }, state, names);

        Assert.Null(Value(duct, -1140324));
        Assert.Equal(-1, Value(duct, -1140333));
        Assert.Equal("Undefined", Value(duct, -1140325));
        Assert.Equal("", Value(duct, -1150468));
        Assert.Equal(3, Value(duct, -1114125));
    }

    [Fact]
    public void PipeSection_IsTheFirstStoredSectionWhenSet_AndSegmentValuesAreTaken()
    {
        var (state, names) = MepState();
        var pipe = new Element { Id = 2 };
        state.ElemClassIdx[2] = 3210;
        var pipeParams = Connectors((40, 9, 0), (40, 9, 0));
        pipeParams.Fields["602:m_idSegment"] = 60;
        state.NativeParams[2] = pipeParams;
        var segment = new TfBlockDeserializer.NativeParams();
        segment.Fields["2968:m_materialId"] = 70;
        segment.Fields["2967:m_scheduleTypeId"] = 80;
        segment.DoubleFields["2968:m_roughness"] = 8.5e-6;
        segment.StringFields["2968:m_description"] = "";
        state.NativeParams[60] = segment;

        ElementBuilder.ApplyMepSystemParams(new RevitFileInfo { Elements = [pipe] }, state, names);

        Assert.Equal(9, Value(pipe, -1114125));
        Assert.Equal(50, Value(pipe, -1140334));
        Assert.Equal(70, Value(pipe, -1140202));
        Assert.Equal(80, Value(pipe, -1140200));
        Assert.Equal(8.5e-6, Value(pipe, -1140204));
        Assert.Equal("", Value(pipe, -1140279));
    }

    [Fact]
    public void SystemTypeAndSystemElements_GetTheirOwnValues()
    {
        var (state, names) = MepState();
        var type = new Element { Id = 50 };
        var system = new Element { Id = 40 };

        ElementBuilder.ApplyMepSystemParams(new RevitFileInfo { Elements = [type, system] }, state, names);

        Assert.Equal("Supply Air", Value(type, -1140325));
        Assert.Equal("Mechanical Supply Air 1", Value(system, -1140324));
    }

    [Fact]
    public void DuctPressureDrop_IsTheLargestTapValue_ElseTheStoredOne()
    {
        var (state, names) = MepState();
        var tapped = new Element { Id = 1 };
        var plain = new Element { Id = 2 };
        state.ElemClassIdx[1] = 3148;
        state.ElemClassIdx[2] = 3148;
        var tappedParams = Connectors((40, 1, 1), (40, 1, 2));
        tappedParams.DoubleFields["3950#0:m_pressureDrop"] = 0.02;
        tappedParams.DoubleFields["3950#0:m_coefficient"] = 0.5;
        tappedParams.DoubleFields["3950#1:m_pressureDrop"] = 0.3;
        tappedParams.DoubleFields["3950#1:m_coefficient"] = 0.7;
        state.NativeParams[1] = tappedParams;
        var plainParams = Connectors((40, 1, 1), (40, 1, 2));
        plainParams.DoubleFields["1219:m_dPressureDrop"] = 0.6;
        plainParams.DoubleFields["1219:m_dLossCoefficient"] = 0.9;
        state.NativeParams[2] = plainParams;

        ElementBuilder.ApplyMepSystemParams(new RevitFileInfo { Elements = [tapped, plain] }, state, names);

        Assert.Equal(0.3, Value(tapped, -1114108));
        Assert.Equal(0.7, Value(tapped, -1114124));
        Assert.Equal(0.6, Value(plain, -1114108));
        Assert.Equal(0.9, Value(plain, -1114124));
    }
}
