using System.Reflection;
using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The lining and panel property definitions the current profile puts in front of the property sets of a door or
/// window style, each made from the lowest instance of the style.</summary>
public class IfcOpeningStyleDefinitionsTests
{
    private static readonly Guid FirstExportId = Guid.Parse("1f34752d-926d-4ab8-8425-c6d81fe02dab");
    private static readonly Guid SecondExportId = Guid.Parse("2a34752d-926d-4ab8-8425-c6d81fe02dab");

    [Fact]
    public void DoorStyle_GetsALiningAndAPanelOfTheFirstDoor()
    {
        string text = Write("IFCDOOR", -2000023, currentProfile: true, firstId: 1, secondId: 2);
        var lining = Assert.Single(Entities(text, "IFCDOORLININGPROPERTIES"));
        var panel = Assert.Single(Entities(text, "IFCDOORPANELPROPERTIES"));
        // GlobalId, OwnerHistory, Name, Description, then LiningDepth ... CasingDepth and the shape aspect style.
        Assert.Equal(15, lining.Args.Length);
        Assert.Equal($"'{IfcGuid.ToIfcGuid(ElementExportIds.SubElementId(FirstExportId, 1))}'", lining.Args[0]);
        Assert.Equal("'Doors:Single:1'", lining.Args[2]);
        Assert.All(lining.Args[3..], argument => Assert.Equal("$", argument));
        Assert.Equal($"'{IfcGuid.ToIfcGuid(ElementExportIds.SubElementId(FirstExportId, 2))}'", panel.Args[0]);
        Assert.Equal("'Doors:Single:1:1'", panel.Args[2]);
        Assert.Equal(["$", "$", ".NOTDEFINED.", "$", ".NOTDEFINED.", "$"], panel.Args[3..]);

        var style = Assert.Single(Entities(text, "IFCDOORSTYLE"));
        Assert.StartsWith($"(#{lining.Id},#{panel.Id}", style.Args[5]);
    }

    [Fact]
    public void WindowStyle_GetsALiningOnly()
    {
        string text = Write("IFCWINDOW", -2000014, currentProfile: true, firstId: 1, secondId: 2);
        var lining = Assert.Single(Entities(text, "IFCWINDOWLININGPROPERTIES"));
        Assert.Equal(13, lining.Args.Length);
        Assert.Equal("'Doors:Single:1'", lining.Args[2]);
        Assert.All(lining.Args[3..], argument => Assert.Equal("$", argument));
        Assert.DoesNotContain("IFCWINDOWPANELPROPERTIES", text);
        Assert.StartsWith($"(#{lining.Id}", Assert.Single(Entities(text, "IFCWINDOWSTYLE")).Args[5]);
    }

    [Fact]
    public void TheStyleIsMadeFromTheLowestInstance_WhateverTheOrderOfTheProducts()
    {
        string text = Write("IFCDOOR", -2000023, currentProfile: true, firstId: 7, secondId: 3);
        Assert.Equal("'Doors:Single:3'", Assert.Single(Entities(text, "IFCDOORLININGPROPERTIES")).Args[2]);
        Assert.Equal($"'{IfcGuid.ToIfcGuid(ElementExportIds.SubElementId(SecondExportId, 1))}'",
            Assert.Single(Entities(text, "IFCDOORLININGPROPERTIES")).Args[0]);
    }

    [Theory]
    [InlineData("IFCDOOR", -2000023)]
    [InlineData("IFCWINDOW", -2000014)]
    public void OlderRelease_WritesNoDefinitions(string product, int category)
    {
        string text = Write(product, category, currentProfile: false, firstId: 1, secondId: 2);
        Assert.DoesNotContain("LININGPROPERTIES", text);
        Assert.DoesNotContain("PANELPROPERTIES", text);
    }

    [Fact]
    public void ProductWithoutAWrittenName_GetsNoDefinitions()
    {
        string text = Write("IFCDOOR", -2000023, currentProfile: true, firstId: 1, secondId: 2, productName: "$");
        Assert.DoesNotContain("LININGPROPERTIES", text);
        Assert.Single(Entities(text, "IFCDOORSTYLE"));
    }

    [Fact]
    public void WithoutAnExportId_TheGlobalIdsAreSynthetic()
    {
        string text = Write("IFCDOOR", -2000023, currentProfile: true, firstId: 1, secondId: 2, exportIds: false);
        var lining = Assert.Single(Entities(text, "IFCDOORLININGPROPERTIES"));
        var panel = Assert.Single(Entities(text, "IFCDOORPANELPROPERTIES"));
        Assert.NotEqual(lining.Args[0], panel.Args[0]);
        Assert.Equal(24, lining.Args[0].Length);
    }

    private static string Write(string product, int category, bool currentProfile, int firstId, int secondId,
        string productName = "'Doors:Single:{0}'", bool exportIds = true)
    {
        Element Instance(int id, Guid exportId) => new()
        {
            Id = id, CategoryId = category, SerializedExportId = exportIds ? exportId : null,
            ValidatedOriginalFamilySymbolId = 601,
            Parameters = [new Parameter
            {
                Id = -1002050, Definition = new Definition { Name = "ELEM_TYPE_PARAM" },
                StorageType = StorageType.ElementId, Value = 500,
            }],
        };
        var elements = new Dictionary<int, Element>
        {
            [500] = new() { Id = 500, Name = "Style", CategoryId = category },
            [601] = new() { Id = 601, Name = "Symbol", CategoryId = category, SerializedExportId = exportIds ? FirstExportId : null },
            [firstId] = Instance(firstId, FirstExportId),
            [secondId] = Instance(secondId, SecondExportId),
        };
        var writer = new StepWriter();
        var products = new Dictionary<int, (int entityId, int placementId, string ifcType)>();
        foreach (int id in new[] { firstId, secondId })
        {
            products.Add(id, (1000 + id, 0, product));
            writer.AddWithId(1000 + id, product, $"'{IfcGuid.FromName($"product:{id}")}',#900,"
                + string.Format(productName, id) + ",$,$,$,$,'" + id + "'");
        }
        var method = typeof(IfcExporter).GetMethod("WriteTypeObjectsAndRelDefinesByTypeCore",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        method.Invoke(null, [writer, 900, elements, products,
            new Dictionary<(string PropertyName, string ValueExpr), int>(), new Dictionary<int, bool?>(), currentProfile]);
        using var text = new StringWriter();
        writer.WriteTo(text, "test.ifc", "IFC2X3", "test", "test");
        return text.ToString();
    }

    private static List<(int Id, string[] Args)> Entities(string text, string type) =>
        text.Split('\n').Select(line => Regex.Match(line.TrimEnd('\r'), $@"^#(\d+)={type}\((.*)\);$"))
            .Where(match => match.Success)
            .Select(match => (int.Parse(match.Groups[1].Value), SplitArguments(match.Groups[2].Value)))
            .ToList();

    private static string[] SplitArguments(string args)
    {
        var result = new List<string>();
        int depth = 0, start = 0;
        bool quoted = false;
        for (int i = 0; i < args.Length; i++)
        {
            char c = args[i];
            if (c == '\'') quoted = !quoted;
            else if (quoted) continue;
            else if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0) { result.Add(args[start..i]); start = i + 1; }
        }
        result.Add(args[start..]);
        return [.. result];
    }
}
