using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

public class StairSupportNameTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void SupportSetOrderCountsFilteredSiblingsAndUsesActualParentName(bool filtered, bool authoredParent)
    {
        var fixture = Build(filtered, authoredParent);
        Apply(fixture);
        string suffix = filtered ? " Stringer 2" : " Stringer 1";
        Assert.Equal(StepWriter.Str(fixture.ParentName + suffix), Name(fixture.Writer, 4));
        Assert.Equal(StepWriter.Str(fixture.ParentName + " Landing 1"), Name(fixture.Writer, 5));
        Assert.Equal("'support ObjectType'", IfcExporter.SplitStepArguments(fixture.Writer.EntityAt(4).Args)[4]);
    }

    [Theory]
    [InlineData("own")] [InlineData("propagated")]
    [InlineData("unknown_scope")] [InlineData("duplicate")]
    [InlineData("expression")] [InlineData("wrong_class")]
    public void SupportsUseOnlyValidatedOwnLiteralOverrides(string mode)
    {
        var fixture = Build(false, false);
        var support = fixture.Elements.Single(e => e.Id == 200);
        var parameter = new Parameter { Definition = new() { Name = "IfcName" }, StorageType = StorageType.String, Value = "authored support" };
        support.Parameters.Add(parameter);
        switch (mode)
        {
            case "own": support.SerializedStairNameParameters!.Add(parameter); break;
            case "unknown_scope": support.SerializedStairNameParameters = null; break;
            case "duplicate": support.SerializedStairNameParameters!.AddRange([parameter,
                new() { Definition = new() { Name = "IfcName" }, StorageType = StorageType.String, Value = "conflicting support" }]); break;
            case "expression": parameter.Value = "{unvalidated expression}"; support.SerializedStairNameParameters!.Add(parameter); break;
            case "wrong_class": support.SerializedOwnElementClassId = 3708; break;
        }
        string old = fixture.Writer.EntityAt(4).Args;
        Apply(fixture);
        string expected = mode switch
        {
            "own" => "authored support",
            "propagated" => fixture.ParentName + " Stringer 1",
            _ => "old support",
        };
        Assert.Equal(StepWriter.Str(expected), Name(fixture.Writer, 4));
        var before = IfcExporter.SplitStepArguments(old);
        var after = IfcExporter.SplitStepArguments(fixture.Writer.EntityAt(4).Args);
        before[2] = after[2]; Assert.Equal(before, after);
    }

    private sealed record Fixture(StepWriter Writer, List<Element> Elements,
        Dictionary<int, (int entityId, int placementId, string ifcType)> Products,
        Dictionary<int, List<int>> Contents, string ParentName);
    private static Fixture Build(bool filtered, bool authoredParent)
    {
        var parent = new Element { Id = 100, CategoryId = -2000120, SerializedOwnElementClassId = 3681,
            SerializedStairTypeId = 101, SerializedStairTypeLinkIsResolved = true, ValidatedStairOccurrenceName = "Лестница",
            SerializedStairCore = filtered ? [new(300, 3727), new(200, 3727), new(400, 3686)] : [new(400, 3686), new(200, 3727)],
            SerializedStairNameParameters = [] };
        if (authoredParent) parent.SerializedStairNameParameters.Add(new()
            { Definition = new() { Name = "IfcName" }, StorageType = StorageType.String, Value = "actual authored parent" });
        var type = new Element { Id = 101, CategoryId = -2000120, SerializedStairsTypeClassId = 3742, ValidatedSystemFamilyName = "Монолитная лестница" };
        var first = new Element { Id = 200, SerializedOwnElementClassId = 3727, SerializedOwningElementId = 100, SerializedStairNameParameters = [] };
        var second = new Element { Id = 300, SerializedOwnElementClassId = 3727, SerializedOwningElementId = 100, SerializedStairNameParameters = [] };
        var landing = new Element { Id = 400, SerializedOwnElementClassId = 3686, SerializedOwningElementId = 100, SerializedStairNameParameters = [] };
        List<Element> elements = filtered ? [second, landing, type, parent, first] : [parent, first, type, landing];
        string name = IfcExporter.ResolveNativeStairName(parent, elements.ToDictionary(e => e.Id), elements.ToDictionary(e => e.Id, _ => 1))!;
        var writer = new StepWriter();
        writer.Add("IFCEXTRUDEDAREASOLID", "$,$,$,$");
        writer.Add("IFCSHAPEREPRESENTATION", "$,'Body','SweptSolid',(#1)");
        writer.Add("IFCPRODUCTDEFINITIONSHAPE", "$,$,(#2)");
        writer.Add("IFCSTAIR", $"'{IfcGuid.FromElementId(100)}',$,{StepWriter.Str(name)},$,'type',#99,#3,'100',.NOTDEFINED.");
        int emitted = filtered ? 300 : 200;
        writer.Add("IFCMEMBER", $"'{IfcGuid.FromElementId(emitted)}',$,'old support',$,'support ObjectType',#99,#3,'{emitted}'");
        writer.Add("IFCSLAB", $"'{IfcGuid.FromElementId(400)}',$,'old landing',$,'landing ObjectType',#99,#3,'400',.LANDING.");
        return new(writer, elements, new() { [100] = (4,99,"IFCSTAIR"), [emitted] = (5,99,"IFCMEMBER"), [400] = (6,99,"IFCSLAB") },
            new() { [1] = [4,5,6] }, name);
    }
    private static void Apply(Fixture fixture) => StairAggregation.Apply(fixture.Writer, fixture.Elements, fixture.Products, fixture.Contents, 88);
    private static string Name(StepWriter writer, int index) => IfcExporter.SplitStepArguments(writer.EntityAt(index).Args)[2];
}
