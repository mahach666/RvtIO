using System.Reflection;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>Blocks captured from sample project P: the flexible pipe 9123013 (its non-BRep steps and Hermite drive curve stopped the walk
/// before the connector manager) and two family instances that mix the piping and electrical connector domains.</summary>
public class VkConnectorBlockTests
{
    private static byte[] Block(string name)
    {
        using Stream stream = TestFiles.OpenBlock($"{name}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static TfBlockDeserializer.NativeParams Walk(string name, byte[]? bytes = null)
    {
        byte[] block = bytes ?? Block(name);
        uint size = BitConverter.ToUInt32(block, 8);
        ushort cls = BitConverter.ToUInt16(block, 12);
        return TfBlockDeserializer.ExtractParams(block, 0, size, cls) ?? throw new InvalidOperationException("walk failed: " + name);
    }

    [SkippableFact]
    public void FlexPipe_WalkReachesBothConnectorsAndTheirCalculations()
    {
        var np = Walk("vk_flex_pipe_9123013.bin");

        Assert.Equal(1, np.Fields["771#0:m_arrRefs@1:index"]);
        Assert.Equal(5924694, np.Fields["771#0:m_arrRefs@1:id"]);
        Assert.Equal(0, np.Fields["771#1:m_arrRefs@0:index"]);
        Assert.Equal(5924699, np.Fields["771#1:m_arrRefs@1:id"]);
        // both connectors carry a segment calculation of the same system
        Assert.Equal(4323215, np.Fields["3556#0:m_idSystem"]);
        Assert.Equal(4323215, np.Fields["3556#1:m_idSystem"]);
        Assert.Equal(3556, np.Fields["calc#0:class"]);
        Assert.Equal(3556, np.Fields["calc#1:class"]);
        Assert.False(np.Fields.ContainsKey("calc#2:class"));
        Assert.Equal(2, np.DoubleFields["87:m_pCrv.nodes"]);
    }

    [SkippableFact]
    public void FlexPipe_HermiteSplineLengthMatchesTheReferenceLength()
    {
        var np = Walk("vk_flex_pipe_9123013.bin");

        double? feet = ElementBuilder.FlexCurveLength(np);

        // The current profile Pset_FlowSegmentPipeSegment.Length of 9123013: 1276.4265031045809 mm.
        Assert.NotNull(feet);
        Assert.Equal(1276.4265031045809, feet.Value * 304.8, 5);
    }

    [Fact]
    public void FlexCurveLength_UsesEveryNodeAndRefusesWhatIsNotACurve()
    {
        static TfBlockDeserializer.NativeParams Nodes(params double[][] nodes)
        {
            var np = new TfBlockDeserializer.NativeParams();
            np.DoubleFields["87:m_pCrv.nodes"] = nodes.Length;
            for (int i = 0; i < nodes.Length; i++)
                for (int k = 0; k < 7; k++) np.DoubleFields[$"87:m_pCrv.node{i}@{k}"] = nodes[i][k];
            return np;
        }

        // two nodes 10 apart on a line with tangents along the line: the Hermite curve is that line
        Assert.Equal(10.0, ElementBuilder.FlexCurveLength(Nodes(
            [0, 0, 0, 0, 1, 0, 0], [10, 10, 0, 0, 1, 0, 0]))!.Value, 9);
        // a third node adds its segment
        Assert.Equal(15.0, ElementBuilder.FlexCurveLength(Nodes(
            [0, 0, 0, 0, 1, 0, 0], [10, 10, 0, 0, 1, 0, 0], [15, 15, 0, 0, 1, 0, 0]))!.Value, 9);
        // a bend between perpendicular tangents is longer than the chord
        Assert.True(ElementBuilder.FlexCurveLength(Nodes(
            [0, 0, 0, 0, 1, 0, 0], [10, 10, 0, 5, 0, 1, 0]))!.Value > 10.0);

        Assert.Null(ElementBuilder.FlexCurveLength(new TfBlockDeserializer.NativeParams()));
        Assert.Null(ElementBuilder.FlexCurveLength(Nodes([0, 0, 0, 0, 1, 0, 0])));                       // one node
        Assert.Null(ElementBuilder.FlexCurveLength(Nodes([0, 0, 0, 0, 1, 0, 0], [0, 10, 0, 0, 1, 0, 0]))); // no parameter span
    }

    [SkippableFact]
    public void FlexPipe_ABlockWithAWrongFooterIsNotWalked()
    {
        byte[] block = Block("vk_flex_pipe_9123013.bin");
        block[^1] ^= 0xFF;

        Assert.Null(TfBlockDeserializer.ExtractParams(block, 0, BitConverter.ToUInt32(block, 8), 3180));
    }

    private static (RevitFileInfo Info, ParsingState State) Graph(int id, string block)
    {
        var state = new ParsingState();
        state.NativeParams[(uint)id] = Walk(block);
        var info = new RevitFileInfo();
        info.Elements.Add(new Element { Id = id });
        ElementBuilder.ApplyConnectorGraph(info, state);
        return (info, state);
    }

    [SkippableFact]
    public void MixedDomainInstance_EachConnectorTakesItsOwnCalculation()
    {
        // 2823056: two piping connectors of system 2452744 and an electrical one (calculations 2382, 2382, 2380).
        var (info, _) = Graph(2823056, "vk_proxy_mixed_domain_2823056.bin");
        Element proxy = info.Elements[0];

        Assert.Equal([1, 2, 3], proxy.SerializedConnectorGraph!.Select(connector => connector.Index).ToArray());
        Assert.Equal([2452744, 2452744, 0], proxy.SerializedConnectorSystems!.ToArray());
        Assert.Equal([0, 0, -1], proxy.SerializedConnectorGraph!.Select(connector => connector.Direction).ToArray());
    }

    [SkippableFact]
    public void MixedDomainInstance_APipingConnectorKeepsItsStoredDirection()
    {
        // 5919313: a piping connector with direction 2 (out, no system) and an electrical one.
        var (info, _) = Graph(5919313, "vk_proxy_directional_5919313.bin");
        Element proxy = info.Elements[0];

        Assert.Equal([2, -1], proxy.SerializedConnectorGraph!.Select(connector => connector.Direction).ToArray());
        Assert.Equal([-1, 0], proxy.SerializedConnectorSystems!.ToArray());
    }

    [SkippableFact]
    public void MixedDomainInstance_WithFewerCalculationsThanConnectorsIsLeftAlone()
    {
        var state = new ParsingState();
        var np = Walk("vk_proxy_mixed_domain_2823056.bin");
        np.Fields.Remove("calc#2:class");     // one connector without a calculation: the one-to-one order is unvalidated
        np.Fields.Remove("2380#0:m_idDownstreamCircuit");
        state.NativeParams[2823056u] = np;
        var info = new RevitFileInfo();
        info.Elements.Add(new Element { Id = 2823056 });

        ElementBuilder.ApplyConnectorGraph(info, state);

        // not one calculation per connector: no calculation is assigned (direction 0 and no system for every connector)
        Assert.Equal([0, 0, 0], info.Elements[0].SerializedConnectorGraph!.Select(connector => connector.Direction).ToArray());
        Assert.Equal([0, 0, 0], info.Elements[0].SerializedConnectorSystems!.ToArray());
    }
}
