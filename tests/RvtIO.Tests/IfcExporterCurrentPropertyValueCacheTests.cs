using RvtIO.Ifc;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The property-value caches of the current profile (the property-set rule): text is shared unless
/// empty, booleans always, small integers, and reals only on the canCacheDouble grid; every other value is an entity
/// of its own. Checked on the current profile references of Test_2022, sample project A and the two MEP files.</summary>
public class IfcExporterCurrentPropertyValueCacheTests
{
    private const string Guid22 = "'3ZGD7y6S5209$mGLi_sPlj'";

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(0.05, true)]
    [InlineData(0.75, true)]
    [InlineData(0.07, false)]
    [InlineData(1.5, true)]
    [InlineData(10.0, true)]
    [InlineData(10.25, false)]
    [InlineData(10.5, true)]
    [InlineData(300.0, true)]
    [InlineData(300.5, false)]
    [InlineData(305.0, true)]
    [InlineData(10300.0, true)]
    [InlineData(10139.999999999929, true)]
    [InlineData(11139.999999996809, false)]
    [InlineData(3.4866666666666668, false)]
    [InlineData(-0.1, true)]
    public void CanCacheDouble_FollowsTheReferenceGrid(double value, bool expected) =>
        Assert.Equal(expected, IfcExporter.CanCacheDoubleCurrent(value));

    [Theory]
    [InlineData("IFCLABEL('Rehau')", true)]
    [InlineData("IFCLABEL('')", false)]
    [InlineData("IFCIDENTIFIER('')", false)]
    [InlineData("IFCIDENTIFIER('Generic 150mm')", true)]
    [InlineData("IFCTEXT('Paint')", true)]
    [InlineData("IFCBOOLEAN(.T.)", true)]
    [InlineData("IFCINTEGER(6)", true)]
    [InlineData("IFCINTEGER(11)", false)]
    [InlineData("IFCINTEGER(-10)", true)]
    // IfcLengthMeasure is tested in feet: 304.8 mm = 1 ft is on the grid, 1000 mm = 3.2808... ft is not.
    [InlineData("IFCLENGTHMEASURE(304.8)", true)]
    [InlineData("IFCLENGTHMEASURE(1000.)", false)]
    // Every other measure is tested as written.
    [InlineData("IFCPOSITIVELENGTHMEASURE(1000.)", true)]
    [InlineData("IFCPOSITIVELENGTHMEASURE(2870.0000004777817)", false)]
    [InlineData("IFCTHERMALTRANSMITTANCEMEASURE(0.75)", true)]
    [InlineData("IFCTHERMALTRANSMITTANCEMEASURE(47.545454545454554)", false)]
    [InlineData("IFCPLANEANGLEMEASURE(90.)", true)]
    [InlineData("IFCAREAMEASURE(1.E1)", true)]
    // A literal of an unknown shape keeps the pooling of the entity writers.
    [InlineData("IFCTHERMALTRANSMITTANCEMEASURE(Infinity.)", true)]
    public void CachesPropertyValue_FollowsTheValueKind(string valueExpr, bool expected) =>
        Assert.Equal(expected, IfcExporter.CachesPropertyValueCurrent(valueExpr));

    private static StepWriter Writer(params (string Type, string Args)[] entities)
    {
        var w = new StepWriter();
        foreach (var (type, args) in entities) w.Add(type, args);
        return w;
    }

    private static IEnumerable<string> ArgsOf(StepWriter w, string type)
    {
        for (int i = 0; i < w.EntityCount; i++)
            if (w.EntityAt(i).Type == type) yield return w.EntityAt(i).Args;
    }

    [Fact]
    public void CacheableValueWrittenTwice_IsOneEntity_AndEverySetPointsAtIt()
    {
        var w = Writer(
            ("IFCPROPERTYSINGLEVALUE", "'Reference',$,IFCIDENTIFIER('Generic 150mm'),$"),
            ("IFCPROPERTYSINGLEVALUE", "'Reference',$,IFCIDENTIFIER('Generic 150mm'),$"),
            ("IFCPROPERTYSET", $"{Guid22},#5,'Pset_A',$,(#1)"),
            ("IFCPROPERTYSET", $"{Guid22},#5,'Pset_B',$,(#2)"));

        Assert.Equal(1, IfcExporter.ApplyCurrentPropertyValueCache(w));

        Assert.Single(ArgsOf(w, "IFCPROPERTYSINGLEVALUE"));
        Assert.Equal(new[] { $"{Guid22},#5,'Pset_A',$,(#1)", $"{Guid22},#5,'Pset_B',$,(#1)" },
            ArgsOf(w, "IFCPROPERTYSET"));
    }

    [Fact]
    public void EmptyLabelSharedByTwoSets_GetsAnEntityPerSet()
    {
        var w = Writer(
            ("IFCPROPERTYSINGLEVALUE", "'FireRating',$,IFCLABEL(''),$"),
            ("IFCPROPERTYSET", $"{Guid22},#5,'Pset_A',$,(#1)"),
            ("IFCPROPERTYSET", $"{Guid22},#5,'Pset_B',$,(#1)"),
            ("IFCPROPERTYSET", $"{Guid22},#5,'Pset_C',$,(#1)"));

        Assert.Equal(0, IfcExporter.ApplyCurrentPropertyValueCache(w));

        Assert.Equal(3, ArgsOf(w, "IFCPROPERTYSINGLEVALUE").Count());
        var references = ArgsOf(w, "IFCPROPERTYSET").Select(args => args[(args.LastIndexOf('(') + 1)..^1]).ToList();
        Assert.Equal(3, references.Distinct().Count());
    }

    [Fact]
    public void RealOffTheGrid_IsNotSharedBetweenSets_AndOnTheGridIs()
    {
        var w = Writer(
            ("IFCPROPERTYSINGLEVALUE", "'Length',$,IFCPOSITIVELENGTHMEASURE(11139.999999996809),$"),
            ("IFCPROPERTYSINGLEVALUE", "'Length',$,IFCPOSITIVELENGTHMEASURE(10300.),$"),
            ("IFCPROPERTYSET", $"{Guid22},#5,'Pset_Duct',$,(#1,#2)"),
            ("IFCPROPERTYSET", $"{Guid22},#5,'Pset_Pipe',$,(#1,#2)"));

        IfcExporter.ApplyCurrentPropertyValueCache(w);

        var values = ArgsOf(w, "IFCPROPERTYSINGLEVALUE").ToList();
        Assert.Equal(3, values.Count);
        Assert.Equal(2, values.Count(args => args.Contains("11139.999999996809")));
        var sets = ArgsOf(w, "IFCPROPERTYSET").ToList();
        Assert.EndsWith("(#1,#2)", sets[0]);
        Assert.EndsWith("(#5,#2)", sets[1]);
    }

    [Fact]
    public void ReferenceToAnotherEntity_PassesThroughUntouched()
    {
        var w = Writer(
            ("IFCPROPERTYSINGLEVALUE", "'Reference',$,IFCIDENTIFIER('A'),$"),
            ("IFCPROPERTYENUMERATEDVALUE", "'Kind',$,(IFCLABEL('x')),$"),
            ("IFCPROPERTYSET", $"{Guid22},#5,'Pset_A',$,(#2,#1)"));

        IfcExporter.ApplyCurrentPropertyValueCache(w);

        Assert.Equal($"{Guid22},#5,'Pset_A',$,(#2,#1)", ArgsOf(w, "IFCPROPERTYSET").Single());
    }
}
