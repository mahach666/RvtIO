using System.Text.Json;
using RvtIO.Parsing;
using RvtIO.Internal;
using Xunit;

namespace RvtIO.Tests;

public class UnitCoreEvaluatorTests
{
    private const string Meters="autodesk.unit.unit:meters-1.0.0";
    private const string Millimeters="autodesk.unit.unit:millimeters-1.0.1";
    private const string Celsius="autodesk.unit.unit:celsius-1.0.1";
    private static TfBlockDeserializer.DocumentSchemaMap Map(params (string Key,string Json)[] records)
        =>new(0,0,records.Select(x=>new TfBlockDeserializer.DocumentSchemaEntry(x.Key,x.Json,0,0)).ToArray());
    private static string Definition(string id,string family,params object[] constants)
        =>JsonSerializer.Serialize(new{typeid=id,inherits=new[]{"autodesk.unit:"+family},constants});
    private static object Parent(string id,string parent)=>new{id,typedValue=new{typeid=parent}};
    private static object Powers(Dictionary<string,int> powers)=>new{id="parentUnits",value=powers};
    private static IReadOnlyDictionary<string,UnitSchemaEvaluator.UnitCore>? Read(params (string,string)[] records)
        =>UnitSchemaEvaluator.EvaluateCore(Map(records),out _);
    [Fact]
    public void CompleteSdkGraphHasSourceBoundVectorsFlagsOffsetAndScale()
    {
        var values=Read();Assert.NotNull(values);Assert.Equal(309,values.Count);
        Assert.Equal(new[]{1,0,0,0,0,0,0,0},values[Meters].Dimensions);
        Assert.Equal(0x400A3F28FCA3F28Ful,BitConverter.DoubleToUInt64Bits(values[Meters].InternalScale));
        Assert.Equal(0x3F6AE06B81AE06B8ul,BitConverter.DoubleToUInt64Bits(values[Millimeters].InternalScale));
        var thermal=values["autodesk.unit.unit:wattsPerSquareMeterKelvin-1.0.1"];
        Assert.Equal(new[]{0,0,1,-3,0,-1,0,0},thermal.Dimensions);Assert.False(thermal.Absolute);Assert.Equal(1.0,thermal.InternalScale);
        Assert.True(values[Celsius].Absolute);Assert.Equal(273.15,values[Celsius].Offset);
    }
    [Fact]
    public void KnownFullKeyRetainsSdkDimensionsAndOffsetBeforeOwnJsonParsing()
    {
        var values=Read((Celsius,"malformed JSON"));Assert.NotNull(values);
        Assert.Equal(273.15,values[Celsius].Offset);Assert.True(values[Celsius].Absolute);
    }
    [Theory]
    [InlineData("length",0)] [InlineData("mass",2)] [InlineData("time",3)]
    [InlineData("electricCurrent",4)] [InlineData("temperature",5)] [InlineData("luminousIntensity",6)]
    public void NativeSixFullDimensionMappingsAreUsed(string name,int index)
    {
        string id="custom:primitive";var values=Read((id,Definition(id,"unit.primitive.dimensional-1.0.0",Parent("dimension","autodesk.unit.dimension:"+name+"-1.0.0"))));
        Assert.NotNull(values);var expected=new int[8];expected[index]=1;Assert.Equal(expected,values[id].Dimensions);
    }
    [Fact]
    public void PrimitiveSpecialAngleLiteralsAndCurrencyHaveDistinctSourceVectors()
    {
        var values=Read()!;
        Assert.Equal(new[]{0,1,0,0,0,0,0,0},values["autodesk.unit.unit:radians-1.0.0"].Dimensions);
        Assert.Equal(new[]{0,0,0,0,0,0,0,1},values["autodesk.unit.unit:steradians-1.0.0"].Dimensions);
        Assert.Equal(new int[8],values["autodesk.unit.unit:currency-1.0.0"].Dimensions);
        Assert.Equal(new int[8],Read(("custom:number",Definition("custom:number","unit.primitive-1.0.0")))!["custom:number"].Dimensions);
    }
    [Fact]
    public void MissingOrUnverifiedDimensionRejectsCoreWithoutInventingLength()
    {
        string id="custom:p";
        Assert.Null(Read((id,Definition(id,"unit.primitive.dimensional-1.0.0"))));
        Assert.Null(Read((id,Definition(id,"unit.primitive.dimensional-1.0.0",Parent("dimension","unknown:dim")))));
        Assert.Null(Read((id,Definition(id,"unit.primitive.dimensional-1.0.0",Parent("dimension","autodesk.unit.dimension:length-1.0.1")))));
    }
    [Fact]
    public void DerivedDimensionUsesOnlyParentUnitsAndDoesNotPropagateAbsoluteFlag()
    {
        string id="custom:derived";
        var values=Read((id,Definition(id,"unit.derived-1.0.0",Powers(new(){[Celsius]=2,[Meters]=-3}),
            new{id="parentFactors",value=new Dictionary<string,int>{{"autodesk.unit.factor:ten-1.0.0",1}}})));
        Assert.NotNull(values);Assert.Equal(new[]{-3,0,0,0,0,2,0,0},values[id].Dimensions);
        Assert.False(values[id].Absolute);Assert.Equal(0.0,values[id].Offset);
    }
    [Fact]
    public void AbsoluteUnitCopiesParentDimensionsButKeepsOnlyItsOwnOffset()
    {
        string id="custom:absolute";
        var values=Read((id,Definition(id,"unit.absolute-1.0.0",Parent("parentUnit",Celsius),new{id="offset",value=12.5})));
        Assert.NotNull(values);Assert.True(values[id].Absolute);Assert.Equal(values[Celsius].Dimensions,values[id].Dimensions);
        Assert.Equal(12.5,values[id].Offset);
        Assert.Equal(0.0,Read((id,Definition(id,"unit.absolute-1.0.0",Parent("parentUnit",Celsius))))![id].Offset);
    }
    [Fact]
    public void WrappedInt32DimensionMultiplicationAndMinimumNegationMatchSource()
    {
        string a="custom:a",b="custom:b";
        var values=Read((a,Definition(a,"unit.derived-1.0.0",Powers(new(){[Meters]=2}))),
            (b,Definition(b,"unit.derived-1.0.0",Powers(new(){[a]=int.MinValue}))));
        Assert.NotNull(values);Assert.Equal(0,values[b].Dimensions[0]);
        Assert.Equal(2.0,UnitSchemaEvaluator.UniversalInternalScale(2.0,int.MinValue));
    }
    [Theory]
    [InlineData(-3)] [InlineData(-1)] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(8)] [InlineData(9)]
    public void UniversalScaleUsesRepeatedMultiplicationInSourceOrder(int exponent)
    {
        double power=1.0;for(int i=0;i<Math.Abs(exponent);i++)power*=BitConverter.UInt64BitsToDouble(0x3FD381D7DBF487FDul);
        double expected=exponent<0?power*1.25:1.25/power;
        Assert.Equal(BitConverter.DoubleToUInt64Bits(expected),BitConverter.DoubleToUInt64Bits(UnitSchemaEvaluator.UniversalInternalScale(1.25,exponent)));
    }
    [Fact]
    public void HugeExponentZeroScaleAndNonfiniteAbsoluteOffsetRejectWholeCore()
    {
        Assert.Throws<InvalidDataException>(()=>UnitSchemaEvaluator.UniversalInternalScale(1,10001));
        string id="custom:u";
        Assert.Null(Read((id,Definition(id,"unit.derived-1.0.0",Powers(new(){[Meters]=1}),new{id="factor",value=0}))));
        string json=Definition(id,"unit.absolute-1.0.0",Parent("parentUnit",Meters),new{id="offset",value=1});
        Assert.Contains("\"id\":\"offset\",\"value\":1}",json);
        Assert.Null(Read((id,json.Replace("\"id\":\"offset\",\"value\":1}","\"id\":\"offset\",\"value\":1e999}"))));
    }
    [Fact]
    public void SourceLinearFormulaPreservesFromNegativeZeroAndTemperatureOffset()
    {
        var values=Read()!;double negativeZero=BitConverter.UInt64BitsToDouble(0x8000000000000000ul);
        Assert.Equal(0x8000000000000000ul,BitConverter.DoubleToUInt64Bits(values[Millimeters].LinearFromInternal(negativeZero)));
        Assert.Equal(0ul,BitConverter.DoubleToUInt64Bits(values[Millimeters].LinearToInternal(negativeZero)));
        Assert.Equal(-273.15,values[Celsius].LinearFromInternal(0));
        Assert.Equal(273.15,values[Celsius].LinearToInternal(0));
    }
    [Fact]
    public void CoreRetentionRevokesRejectedCoreButKeepsValidSiAndRawMaps()
    {
        var state=new ParsingState();using var diag=new StringWriter();
        TfBlockDeserializer.DocumentSchemaPrefix Prefix(params (string,string)[] records)=>new(0,0,0,Map(records),Map(),[],[],[]);
        RvtFileReader.RecordOwnDocumentSchemas(state,Prefix(),diag);Assert.Equal(309,state.OwnDocumentUnitCores!.Count);
        RvtFileReader.RecordOwnDocumentSchemas(state,Prefix(("custom:p",Definition("custom:p","unit.primitive.dimensional-1.0.0"))),diag);
        Assert.Null(state.OwnDocumentUnitCores);Assert.NotNull(state.OwnDocumentSiFactors);Assert.NotNull(state.OwnDocumentSchemas);
        Assert.Contains("unit core unavailable",diag.ToString());
        RvtFileReader.RecordOwnDocumentSchemas(state,null,diag);Assert.Null(state.OwnDocumentUnitCores);
    }
}
