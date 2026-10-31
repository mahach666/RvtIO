using System.Text.Json;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class UnitSchemaEvaluatorTests
{
    private const string Meters = "autodesk.unit.unit:meters-1.0.0";
    private const string Millimeters = "autodesk.unit.unit:millimeters-1.0.1";
    private const string Thermal = "autodesk.unit.unit:wattsPerSquareMeterKelvin-1.0.1";
    private static TfBlockDeserializer.DocumentSchemaMap Map(params (string Key, string Json)[] values)
        => new(0,0,values.Select(x=>new TfBlockDeserializer.DocumentSchemaEntry(x.Key,x.Json,0,0)).ToArray());
    private static string Definition(string id, string family, params (string Id, object Value)[] constants)
        => JsonSerializer.Serialize(new { typeid=id, inherits=new[]{"autodesk.unit:"+family},
            constants=constants.Select(c=>new{id=c.Id,value=c.Value}).ToArray() });
    private static (string,string) Unit(string id, object? factor=null, Dictionary<string,object>? units=null, Dictionary<string,object>? factors=null)
    {
        var constants=new List<(string,object)>();
        if(factor!=null)constants.Add(("factor",factor));
        if(units!=null)constants.Add(("parentUnits",units));
        if(factors!=null)constants.Add(("parentFactors",factors));
        return(id,Definition(id,"unit.derived-1.0.0",constants.ToArray()));
    }
    private static IReadOnlyDictionary<string,double>? Read(params (string,string)[] entries)
        => UnitSchemaEvaluator.EvaluateOwn(Map(entries),out _);

    [Fact]
    public void EmptyOwnInputRetainsFullSdkNumericDefinitionsNotPublicCoefficients()
    {
        var values=Read();Assert.NotNull(values);Assert.Equal(309,values.Count);
        Assert.Equal(0x3F50624DD2F1A9FCul,BitConverter.DoubleToUInt64Bits(values[Millimeters]));
        Assert.Equal(1.0,values[Meters]);Assert.Equal(1.0,values[Thermal]);Assert.NotEqual(304.8,values[Millimeters]);
    }
    [Fact]
    public void KnownFullKeySuppressesDifferentOrMalformedOwnJsonBeforeParsing()
    {
        var different=Unit(Millimeters,2.0,new(){[Meters]=1});
        var baseline=Read()!;
        Assert.Equal(baseline[Millimeters],Read(different)![Millimeters]);
        Assert.Equal(baseline[Millimeters],Read((Millimeters,"not JSON"))![Millimeters]);
    }
    [Fact]
    public void FullVersionAndCaseAreNotFoldedAndOpaqueKeysRemainExact()
    {
        string[] keys=["custom:Length-1.0.0","custom:Length-1.0.1","custom:length-1.0.0","\u0361utodesk.custom:я\U0001F600-1.0.0"];
        var values=Read(keys.Select((k,i)=>Unit(k,i+2,new(){[Meters]=1})).ToArray());Assert.NotNull(values);
        for(int i=0;i<keys.Length;i++)Assert.Equal(i+2,values[keys[i]]);
    }
    [Fact]
    public void ComponentMissingFactorUsesValidatedCtorOneNotMmDefault()
    {
        var values=Read(Unit("custom:u-1.0.0",units:new(){[Meters]=1}));Assert.NotNull(values);Assert.Equal(1.0,values["custom:u-1.0.0"]);
        string factor=Definition("custom:f","factor.derived-1.0.0",("parentFactors",new Dictionary<string,object>{{"autodesk.unit.factor:ten-1.0.0",-3}}));
        values=Read(("custom:f",factor),Unit("custom:u",units:new(){[Meters]=1},factors:new(){["custom:f"]=1}));
        Assert.Equal(0.001,values!["custom:u"]);
    }
    [Theory]
    [InlineData(-3)] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void SignedParentPowersAndSeparateFactorMapAreApplied(int power)
    {
        var values=Read(Unit("custom:u",3.0,new(){[Meters]=1},new(){["autodesk.unit.factor:ten-1.0.0"]=power}));
        Assert.NotNull(values);Assert.Equal(3.0*Math.Pow(10,power),values["custom:u"]);
    }
    [Fact]
    public void WrongTypedAndMissingParentRejectWholeProjectionIncludingPowerZero()
    {
        Assert.Null(Read(Unit("custom:u",units:new(){["autodesk.unit.factor:ten-1.0.0"]=1})));
        Assert.Null(Read(Unit("custom:u",units:new(){["missing"]=0})));
        Assert.Null(Read(Unit("custom:u",units:new(){[Meters]=1},factors:new(){[Meters]=0})));
    }
    [Fact]
    public void CyclesRejectEvenWhenPowerZero()
    {
        Assert.Null(Read(Unit("custom:u",units:new(){["custom:u"]=0})));
        Assert.Null(Read(Unit("custom:a",units:new(){["custom:b"]=1}),Unit("custom:b",units:new(){["custom:a"]=1})));
        var f=Definition("custom:f","factor.derived-1.0.0",("parentFactors",new Dictionary<string,object>{{"custom:f",0}}));
        Assert.Null(Read(("custom:f",f),Unit("custom:u",units:new(){[Meters]=1},factors:new(){["custom:f"]=1})));
    }
    [Theory]
    [InlineData("2.0")] [InlineData("2e0")] [InlineData("2147483648")] [InlineData("true")]
    public void PowersMustBeOriginalSignedInt32JsonShape(string number)
    {
        string json="{\"typeid\":\"custom:u\",\"inherits\":[\"autodesk.unit:unit.derived-1.0.0\"],\"constants\":[{\"id\":\"parentUnits\",\"value\":{\""+Meters+"\":"+number+"}}]}";
        Assert.Null(Read(("custom:u",json)));
    }
    [Fact]
    public void DuplicateInputJsonAndConstantsRejectWithoutPartialResults()
    {
        var u=Unit("custom:u",units:new(){[Meters]=1});Assert.Null(Read(u,u));
        Assert.Null(Read(("custom:u",u.Item2.Replace("\"typeid\":","\"typeid\":\"custom:u\",\"typeid\":"))));
        string duplicated=Definition("custom:u","unit.derived-1.0.0",("factor",1), ("factor",2), ("parentUnits",new Dictionary<string,object>{{Meters,1}}));
        Assert.Null(Read(("custom:u",duplicated)));
    }
    [Fact]
    public void MalformedIdentityFamiliesOrParentsRejectWithReason()
    {
        string[] invalid=["null","[]","{}","{\"typeid\":\"wrong\",\"inherits\":[\"autodesk.unit:unit.derived-1.0.0\"]}",
            Definition("custom:u","unit.derived-2.0.0"),Definition("custom:u","unit.derived-1.0.0"),
            Definition("custom:u","unit.derived-1.0.0",("parentUnits",new Dictionary<string,object>())),
            Definition("custom:u","unit.derived-1.0.0",("parentUnits","not a map"))];
        foreach(string json in invalid)
        {Assert.Null(UnitSchemaEvaluator.EvaluateOwn(Map(("custom:u",json)),out string? error));Assert.False(string.IsNullOrEmpty(error));}
    }
    [Fact]
    public void NegativeZeroScalarBitsAreNotNormalized()
    {
        string json=Unit("custom:u",1.0,new(){[Meters]=1}).Item2;
        Assert.Contains("\"id\":\"factor\",\"value\":1}",json);
        json=json.Replace("\"id\":\"factor\",\"value\":1}","\"id\":\"factor\",\"value\":-0.0}");
        var values=Read(("custom:u",json));Assert.NotNull(values);Assert.Equal(0x8000000000000000ul,BitConverter.DoubleToUInt64Bits(values["custom:u"]));
    }
    [Fact]
    public void AbsoluteSiFactorFollowsParentButDoesNotApplyTemperatureOffset()
    {
        string json=JsonSerializer.Serialize(new {typeid="custom:absolute",inherits=new[]{"autodesk.unit:unit.absolute-1.0.0"},
            constants=new object[]{new{id="parentUnit",typedValue=new{typeid=Millimeters}},new{id="offset",value=273.15}}});
        Assert.Equal(0.001,Read(("custom:absolute",json))!["custom:absolute"]);
    }
    [Fact]
    public void NonfiniteScalarAndOverflowAreExplicitRejectedSubset()
    {
        var u=Unit("custom:u",1.0,new(){[Meters]=1});
        Assert.Contains("\"id\":\"factor\",\"value\":1}",u.Item2);
        Assert.Null(Read(("custom:u",u.Item2.Replace("\"id\":\"factor\",\"value\":1}","\"id\":\"factor\",\"value\":1e999}"))));
        Assert.Null(Read(Unit("custom:u",double.MaxValue,new(){[Meters]=1},new(){["autodesk.unit.factor:ten-1.0.0"]=2})));
    }
    [Fact]
    public void ParentOrderUsesUtf8BytesNotUtf16OrInsertionOrder()
    {
        string a="custom:\uE000", b="custom:\U00010000";
        var values=Read((a,Definition(a,"factor-1.0.0",("factor",1e-308))),
            (b,Definition(b,"factor-1.0.0",("factor",1e308))),
            Unit("custom:u",1e308,new(){[Meters]=1},new(){[b]=1,[a]=1}));
        Assert.NotNull(values);
        Assert.Equal(BitConverter.DoubleToUInt64Bits((1e308*1e-308)*1e308),BitConverter.DoubleToUInt64Bits(values["custom:u"]));
    }
    [Theory]
    [InlineData(100,true)] [InlineData(101,false)]
    public void UncachedDependencyDepthBoundaryIsExplicitRestrictedProjection(int count,bool accepted)
    {
        var entries=Enumerable.Range(0,count).Select(i=>Unit("custom:n"+i,units:new(){[i==count-1?Meters:"custom:n"+(i+1)]=1})).ToArray();
        Assert.Equal(accepted,Read(entries)!=null);
    }
    private static TfBlockDeserializer.DocumentSchemaPrefix Prefix(params (string,string)[] entries)
        =>new(0,0,0,Map(entries),Map(),[],[],[]);
    [Fact]
    public void ReaderRetentionKeepsRawMapsSeparateFromNumericProjection()
    {
        var state=new RvtIO.Internal.ParsingState();var prefix=Prefix();
        RvtIO.RvtFileReader.RecordOwnDocumentSchemas(state,prefix,null);
        Assert.Same(prefix.Units,state.OwnDocumentSchemas!.Value.Units);
        Assert.Equal(309,state.OwnDocumentSiFactors!.Count);
        Assert.Null(state.OwnDocumentUnitOptions);
    }
    [Fact]
    public void RejectedProjectionRevokesPriorNumbersButRetainsOwnVerbatimMapsAndReason()
    {
        var state=new RvtIO.Internal.ParsingState();using var diag=new StringWriter();
        RvtIO.RvtFileReader.RecordOwnDocumentSchemas(state,Prefix(),diag);
        var bad=Prefix(Unit("custom:u",units:new(){["missing"]=1}));
        RvtIO.RvtFileReader.RecordOwnDocumentSchemas(state,bad,diag);
        Assert.Null(state.OwnDocumentSiFactors);Assert.Same(bad.Units,state.OwnDocumentSchemas!.Value.Units);
        Assert.Contains("Missing full unit dependency: missing",diag.ToString());
    }
    [Fact]
    public void MissingPrefixRevokesBothMapsAndProjectionWithoutInventingSdkCurrentUnits()
    {
        var state=new RvtIO.Internal.ParsingState();
        RvtIO.RvtFileReader.RecordOwnDocumentSchemas(state,Prefix(),null);
        RvtIO.RvtFileReader.RecordOwnDocumentSchemas(state,null,null);
        Assert.Null(state.OwnDocumentSchemas);Assert.Null(state.OwnDocumentSiFactors);
    }
}
