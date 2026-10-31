using RvtIO.Ifc;
using Xunit;

namespace RvtIO.Tests;

/// <summary>
/// Property set and relation GlobalIds derived from exact product GlobalIds
/// The vector is Test_2022 wall 3lLx0gNe59vvExhby0Bfew
/// and its reference Pset_WallCommon.
/// </summary>
public class PropertySetGlobalIdTests
{
    private const string Wall = "3lLx0gNe59vvExhby0Bfew";
    private const string ReferencePropertySet = "1ZBwgPdweDGg_Me8tKh$Yr";
    private const string ReferenceRelation = "07nX1IewLOdxT5jWd_KeRT";

    [Fact]
    public void FromMd5_ReproducesTheReferencePropertySetAndRelation()
    {
        Assert.Equal(ReferencePropertySet, IfcGuid.FromMd5($"IfcPropertySet:Pset_WallCommon:{Wall}"));
        Assert.Equal(ReferenceRelation, IfcGuid.FromMd5($"IfcRelDefinesByProperties::{ReferencePropertySet}"));
    }

    [Fact]
    public void PropertySetOfAnExactProduct_GetsTheAutodeskGlobalIds()
    {
        var (w, propertySet, relation) = Model(Wall, "Pset_WallCommon");
        Assert.Equal(1, IfcExporter.ApplyPropertySetGlobalIds(w, new HashSet<string> { Wall }));
        Assert.StartsWith($"'{ReferencePropertySet}',#1,'Pset_WallCommon'", Args(w, propertySet));
        Assert.Equal($"'{ReferenceRelation}',#1,$,$,(#2),#{propertySet}", Args(w, relation));
    }

    [Fact]
    public void ProductWithoutAnExactGlobalId_KeepsItsPropertySetGlobalIds()
    {
        var (w, propertySet, relation) = Model(Wall, "Pset_WallCommon");
        string before = Args(w, propertySet) + Args(w, relation);
        Assert.Equal(0, IfcExporter.ApplyPropertySetGlobalIds(w, new HashSet<string>()));
        Assert.Equal(before, Args(w, propertySet) + Args(w, relation));
    }

    [Fact]
    public void SharedPropertySet_NamedRelationOrSeveralObjects_AreLeftUnchanged()
    {
        var (shared, propertySet, _) = Model(Wall, "Pset_WallCommon");
        shared.Add("IFCWALLTYPE", $"'{IfcGuid.FromName("type")}',#1,'T',$,$,(#{propertySet}),$,$,$,.STANDARD.");
        Assert.Equal(0, IfcExporter.ApplyPropertySetGlobalIds(shared, new HashSet<string> { Wall }));

        var named = new StepWriter();
        named.Add("IFCOWNERHISTORY", "$,$,$,.NOCHANGE.,$,$,$,0");
        named.Add("IFCWALL", $"'{Wall}',#1,$,$,$,$,$,$");
        int set = named.Add("IFCPROPERTYSET", $"'{IfcGuid.FromName("pset")}',#1,'Pset_WallCommon',$,()");
        named.Add("IFCRELDEFINESBYPROPERTIES", $"'{IfcGuid.FromName("rel")}',#1,'Name',$,(#2),#{set}");
        Assert.Equal(0, IfcExporter.ApplyPropertySetGlobalIds(named, new HashSet<string> { Wall }));

        var several = new StepWriter();
        several.Add("IFCOWNERHISTORY", "$,$,$,.NOCHANGE.,$,$,$,0");
        several.Add("IFCWALL", $"'{Wall}',#1,$,$,$,$,$,$");
        several.Add("IFCWALL", $"'{IfcGuid.FromName("other")}',#1,$,$,$,$,$,$");
        set = several.Add("IFCPROPERTYSET", $"'{IfcGuid.FromName("pset")}',#1,'Pset_WallCommon',$,()");
        several.Add("IFCRELDEFINESBYPROPERTIES", $"'{IfcGuid.FromName("rel")}',#1,$,$,(#2,#3),#{set}");
        Assert.Equal(0, IfcExporter.ApplyPropertySetGlobalIds(several, new HashSet<string> { Wall }));
    }

    [Fact]
    public void GlobalIdAlreadyInTheFile_LeavesBothEntitiesUnchanged()
    {
        var (w, _, _) = Model(Wall, "Pset_WallCommon");
        w.Add("IFCPROPERTYSET", $"'{ReferencePropertySet}',#1,'Other',$,()");
        Assert.Equal(0, IfcExporter.ApplyPropertySetGlobalIds(w, new HashSet<string> { Wall }));
    }

    [Fact]
    public void PropertySetName_IsHashedAsTheDecodedText()
    {
        const string name = "Свойства 'O'";
        var (w, propertySet, _) = Model(Wall, name);
        Assert.Equal(1, IfcExporter.ApplyPropertySetGlobalIds(w, new HashSet<string> { Wall }));
        Assert.StartsWith($"'{IfcGuid.FromMd5($"IfcPropertySet:{name}:{Wall}")}'", Args(w, propertySet));
    }

    [Theory]
    [InlineData("Pset_WallCommon")]
    [InlineData("Свойства 'O' \\ x")]
    [InlineData("")]
    [InlineData("Rebar Bar:Ø10, Dmin=4ϕ:17")]
    [InlineData("a\r\nb")]
    public void Unescape_InvertsStr(string text)
    {
        string written = StepWriter.Str(text);
        Assert.Equal(text, StepWriter.Unescape(written[1..^1]));
    }

    [Theory]
    [InlineData("Ø10", "'\\X\\D810'")]
    [InlineData("Ø10, Dmin=4ϕ", "'\\X\\D810, Dmin=4\\X2\\03D5\\X0\\'")]
    [InlineData("ü\r\n", "'\\X\\FC\\X\\0D\\X\\0A'")]
    [InlineData("Ж®µ", "'\\X2\\0416\\X0\\\\X\\AE\\X\\B5'")]
    [InlineData("ЖßЖ", "'\\X2\\0416\\X0\\\\X\\DF\\X2\\0416\\X0\\'")]
    public void Str_WritesALatin1CharacterAsOneByteAndTheRestAsUtf16Runs(string text, string expected) => Assert.Equal(expected, StepWriter.Str(text));

    [Theory]
    [InlineData("a'b")]
    [InlineData("\\X2\\041")]
    [InlineData("\\X\\A")]
    [InlineData("\\X\\GG")]
    [InlineData("\\S\\A")]
    public void Unescape_RejectsTextStrNeverWrites(string escaped) => Assert.Null(StepWriter.Unescape(escaped));

    private static (StepWriter Writer, int PropertySet, int Relation) Model(string productGlobalId, string name)
    {
        var w = new StepWriter();
        w.Add("IFCOWNERHISTORY", "$,$,$,.NOCHANGE.,$,$,$,0");
        w.Add("IFCWALLSTANDARDCASE", $"'{productGlobalId}',#1,'Wall',$,$,$,$,'1'");
        int value = w.Add("IFCPROPERTYSINGLEVALUE", "'LoadBearing',$,IFCBOOLEAN(.T.),$");
        int propertySet = w.Add("IFCPROPERTYSET", $"'{IfcGuid.FromName("pset")}',#1,{StepWriter.Str(name)},$,(#{value})");
        int relation = w.Add("IFCRELDEFINESBYPROPERTIES", $"'{IfcGuid.FromName("rel")}',#1,$,$,(#2),#{propertySet}");
        return (w, propertySet, relation);
    }

    private static string Args(StepWriter w, int id)
    {
        for (int i = 0; i < w.EntityCount; i++)
            if (w.EntityAt(i).Id == id) return w.EntityAt(i).Args;
        throw new KeyNotFoundException(id.ToString());
    }
}
