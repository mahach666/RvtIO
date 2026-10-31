using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The order of the IfcOpeningElements of a host and their ObjectType in the current profile.</summary>
public class IfcExporterOpeningOrderRecessCurrentTests
{
    private static (StepWriter W, int Owner, int Placement) NewWriter()
    {
        var w = new StepWriter();
        int owner = w.Add("IFCOWNERHISTORY", "$,$,$,.NOCHANGE.,$,$,$,0");
        int origin = w.Add("IFCCARTESIANPOINT", "(0.,0.,0.)");
        int axis = w.Add("IFCAXIS2PLACEMENT3D", $"#{origin},$,$");
        int placement = w.Add("IFCLOCALPLACEMENT", $"$,#{axis}");
        return (w, owner, placement);
    }

    private static int AddExtrusionBody(StepWriter w, double x, double y, double depth)
    {
        int origin = w.Add("IFCCARTESIANPOINT", "(0.,0.)");
        int profilePlacement = w.Add("IFCAXIS2PLACEMENT2D", $"#{origin},$");
        int profile = w.Add("IFCRECTANGLEPROFILEDEF", $".AREA.,$,#{profilePlacement},{StepWriter.Real(x)},{StepWriter.Real(y)}");
        int point3 = w.Add("IFCCARTESIANPOINT", "(0.,0.,0.)");
        int position = w.Add("IFCAXIS2PLACEMENT3D", $"#{point3},$,$");
        int direction = w.Add("IFCDIRECTION", "(0.,0.,1.)");
        int solid = w.Add("IFCEXTRUDEDAREASOLID", $"#{profile},#{position},#{direction},{StepWriter.Real(depth)}");
        int representation = w.Add("IFCSHAPEREPRESENTATION", $"#1,'Body','SweptSolid',(#{solid})");
        return w.Add("IFCPRODUCTDEFINITIONSHAPE", $"$,$,(#{representation})");
    }

    private static int AddBrepBody(StepWriter w)
    {
        int brep = w.Add("IFCFACETEDBREP", "#1");
        int representation = w.Add("IFCSHAPEREPRESENTATION", $"#1,'Body','Brep',(#{brep})");
        return w.Add("IFCPRODUCTDEFINITIONSHAPE", $"$,$,(#{representation})");
    }

    private static int AddProduct(StepWriter w, string type, string name, int owner, int placement, int? shape = null) =>
        w.Add(type, $"'{name}',#{owner},'{name}',$,$,#{placement},{(shape is { } id ? "#" + id : "$")},'1'");

    private static int AddOpening(StepWriter w, int owner, int placement, int host, string name, int? shape = null)
    {
        int opening = AddProduct(w, "IFCOPENINGELEMENT", name, owner, placement, shape);
        w.Add("IFCRELVOIDSELEMENT", $"'v{name}',#{owner},$,$,#{host},#{opening}");
        return opening;
    }

    private static List<string> OpeningNames(StepWriter w) =>
        Enumerable.Range(0, w.EntityCount).Select(w.EntityAt).Where(e => e.Type == "IFCOPENINGELEMENT")
            .Select(e => IfcExporter.SplitStepArguments(e.Args)[0].Trim('\'')).ToList();

    [Fact]
    public void Order_CuttersByOpeningIndexThenOwnLoopsThenDelayedDoorsByDoorId()
    {
        var (w, owner, placement) = NewWriter();
        int host = AddProduct(w, "IFCWALLSTANDARDCASE", "host", owner, placement);
        // The order the writers of this exporter make them in: a door's opening with the door, the pair openings after.
        int doorB = AddOpening(w, owner, placement, host, "doorB");
        int doorA = AddOpening(w, owner, placement, host, "doorA");
        int cutter3 = AddOpening(w, owner, placement, host, "cutter3");
        int loop2 = AddOpening(w, owner, placement, host, "loop2");
        int cutter2 = AddOpening(w, owner, placement, host, "cutter2");
        int loop1 = AddOpening(w, owner, placement, host, "loop1");
        var keys = new Dictionary<int, (int Group, int Major, int Minor)>
        {
            [doorB] = (IfcExporter.OpeningOrderDelayed, 500, 0), [doorA] = (IfcExporter.OpeningOrderDelayed, 200, 0),
            [cutter3] = (IfcExporter.OpeningOrderCutter, 3, 1), [cutter2] = (IfcExporter.OpeningOrderCutter, 2, 1),
            [loop2] = (IfcExporter.OpeningOrderOwnLoop, 2, 0), [loop1] = (IfcExporter.OpeningOrderOwnLoop, 1, 0),
        };

        Assert.Equal(1, IfcExporter.ApplyCurrentOpeningOrder(w, keys));

        Assert.Equal(["cutter2", "cutter3", "loop1", "loop2", "doorA", "doorB"], OpeningNames(w));
    }

    [Fact]
    public void Order_KeepsIdsWithTheirEntitiesAndTheOtherEntitiesInPlace()
    {
        var (w, owner, placement) = NewWriter();
        int host = AddProduct(w, "IFCWALLSTANDARDCASE", "host", owner, placement);
        int second = AddOpening(w, owner, placement, host, "second");
        int marker = w.Add("IFCCARTESIANPOINT", "(1.,2.,3.)");
        int first = AddOpening(w, owner, placement, host, "first");
        var keys = new Dictionary<int, (int Group, int Major, int Minor)>
        {
            [second] = (IfcExporter.OpeningOrderCutter, 2, 1), [first] = (IfcExporter.OpeningOrderCutter, 1, 1),
        };

        IfcExporter.ApplyCurrentOpeningOrder(w, keys);

        var lines = Enumerable.Range(0, w.EntityCount).Select(w.EntityAt).ToList();
        int firstPosition = lines.FindIndex(e => e.Id == first), secondPosition = lines.FindIndex(e => e.Id == second);
        Assert.True(firstPosition < secondPosition);
        Assert.Equal("(1.,2.,3.)", lines.Single(e => e.Id == marker).Args);
        Assert.Equal(marker, lines[lines.FindIndex(e => e.Type == "IFCCARTESIANPOINT" && e.Args == "(1.,2.,3.)")].Id);
        Assert.Contains("'first'", lines.Single(e => e.Id == first).Args);
        Assert.Contains("'second'", lines.Single(e => e.Id == second).Args);
    }

    [Fact]
    public void Order_OpeningsWithoutAKeyFollowTheOthers_AndAFilledOneCountsAsDelayed()
    {
        var (w, owner, placement) = NewWriter();
        int host = AddProduct(w, "IFCWALLSTANDARDCASE", "host", owner, placement);
        int unknown = AddOpening(w, owner, placement, host, "unknown");
        int filled = AddOpening(w, owner, placement, host, "filled");
        int door = AddProduct(w, "IFCDOOR", "door", owner, placement);
        w.Add("IFCRELFILLSELEMENT", $"'f',#{owner},$,$,#{filled},#{door}");
        int cutter = AddOpening(w, owner, placement, host, "cutter");
        var keys = new Dictionary<int, (int Group, int Major, int Minor)> { [cutter] = (IfcExporter.OpeningOrderCutter, 1, 1) };

        IfcExporter.ApplyCurrentOpeningOrder(w, keys);

        Assert.Equal(["cutter", "filled", "unknown"], OpeningNames(w));
    }

    [Fact]
    public void Order_AHostInTheRightOrderIsLeftAlone_AndHostsDoNotMix()
    {
        var (w, owner, placement) = NewWriter();
        int hostA = AddProduct(w, "IFCWALLSTANDARDCASE", "hostA", owner, placement);
        int hostB = AddProduct(w, "IFCWALLSTANDARDCASE", "hostB", owner, placement);
        int a1 = AddOpening(w, owner, placement, hostA, "a1");
        int b2 = AddOpening(w, owner, placement, hostB, "b2");
        int a2 = AddOpening(w, owner, placement, hostA, "a2");
        int b1 = AddOpening(w, owner, placement, hostB, "b1");
        var keys = new Dictionary<int, (int Group, int Major, int Minor)>
        {
            [a1] = (IfcExporter.OpeningOrderCutter, 1, 1), [a2] = (IfcExporter.OpeningOrderCutter, 2, 1),
            [b1] = (IfcExporter.OpeningOrderCutter, 1, 1), [b2] = (IfcExporter.OpeningOrderCutter, 2, 1),
        };

        Assert.Equal(1, IfcExporter.ApplyCurrentOpeningOrder(w, keys));

        // hostA is in order; hostB swaps within its own places, between the places of hostA's openings.
        Assert.Equal(["a1", "b1", "a2", "b2"], OpeningNames(w));
    }

    [Fact]
    public void PlaceEntities_RejectsWhatIsNoPermutationOfThePlaces()
    {
        var (w, _, _) = NewWriter();
        Assert.Throws<ArgumentException>(() => w.PlaceEntities([0, 1], [0, 2]));
        Assert.Throws<ArgumentException>(() => w.PlaceEntities([0, 1], [0]));
    }

    private static HashSet<int> Recess(StepWriter w, IReadOnlyDictionary<int, (int Group, int Major, int Minor)>? keys = null,
        IReadOnlySet<int>? writerRecess = null) =>
        new IfcExporter().OpeningRecessEntitiesCurrent(w, new Dictionary<int, Element>(), new Dictionary<int, int>(),
            new Dictionary<int, (int, int, string)>(), keys ?? new Dictionary<int, (int, int, int)>(), writerRecess ?? new HashSet<int>());

    private static (StepWriter W, int Owner, int Placement, int Host) WallOfWidth(double width)
    {
        var (w, owner, placement) = NewWriter();
        int host = AddProduct(w, "IFCWALLSTANDARDCASE", "host", owner, placement, AddExtrusionBody(w, 5.0, width, 3.0));
        return (w, owner, placement, host);
    }

    [Theory]
    [InlineData(0.100, true)]
    [InlineData(0.202, false)]
    [InlineData(0.2020000000009, false)]
    [InlineData(0.300, false)]
    public void Recess_AnExtrudedOpeningShorterThanTheWallWidthIsARecess(double depth, bool expected)
    {
        var (w, owner, placement, host) = WallOfWidth(0.202);
        int opening = AddOpening(w, owner, placement, host, "o", AddExtrusionBody(w, 0.8, depth, 2.1));
        // (extruded 2.1 along the model Z, the profile 0.8 x depth: the vertical box of a cutter across the wall)
        Assert.Equal(expected, Recess(w).Contains(opening));
    }

    [Fact]
    public void Recess_ABrepOpeningIsNeverARecess_EvenWhenItsWriterThoughtItWasShallow()
    {
        var (w, owner, placement, host) = WallOfWidth(0.202);
        int opening = AddOpening(w, owner, placement, host, "o", AddBrepBody(w));
        Assert.DoesNotContain(opening, Recess(w, writerRecess: new HashSet<int> { opening }));
    }

    [Fact]
    public void Recess_TheOpeningOfAHostsOwnSketchLoopIsNeverARecess()
    {
        var (w, owner, placement, host) = WallOfWidth(0.202);
        int opening = AddOpening(w, owner, placement, host, "o", AddExtrusionBody(w, 0.8, 0.100, 2.1));
        var keys = new Dictionary<int, (int Group, int Major, int Minor)> { [opening] = (IfcExporter.OpeningOrderOwnLoop, 1, 0) };
        Assert.DoesNotContain(opening, Recess(w, keys));
    }

    [Fact]
    public void Recess_AnOpeningWhoseDepthCannotBeReadKeepsTheDecisionOfItsWriter_UnlessItIsABrep()
    {
        var (w, owner, placement, host) = WallOfWidth(0.202);
        int unreadable = AddOpening(w, owner, placement, host, "unreadable");
        var recess = Recess(w, writerRecess: new HashSet<int> { unreadable });
        Assert.Contains(unreadable, recess);
    }
}
