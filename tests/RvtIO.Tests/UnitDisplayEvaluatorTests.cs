using System.Text.Json;
using RvtIO.Internal;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class UnitDisplayEvaluatorTests
{
    private const string Prefix = "autodesk.unit.unit:";
    private static TfBlockDeserializer.DocumentSchemaMap Map(params (string Key,string Json)[] entries)
        => new(0,0,entries.Select(x => new TfBlockDeserializer.DocumentSchemaEntry(x.Key,x.Json,0,0)).ToArray());
    private static UnitSchemaEvaluator.UnitConversions Read(params (string,string)[] entries)
        => UnitSchemaEvaluator.EvaluateDisplay(Map(entries),out string? failure) ?? throw new InvalidDataException(failure);
    private static string Derived(string id,string parent,double factor=1.0)
        => JsonSerializer.Serialize(new { typeid=id,inherits=new[]{"autodesk.unit:unit.derived-1.0.0"},
            constants=new object[]{ new{id="parentUnits",value=new Dictionary<string,int>{{parent,1}}}, new{id="factor",value=factor} } });
    private static ulong From(UnitSchemaEvaluator.UnitConversions units,string id,double value)
    {
        Assert.True(units.TryFromInternal(value,id,out double converted));return BitConverter.DoubleToUInt64Bits(converted);
    }
    private static ulong To(UnitSchemaEvaluator.UnitConversions units,string id,double value)
    {
        Assert.True(units.TryToInternal(value,id,out double converted));return BitConverter.DoubleToUInt64Bits(converted);
    }

    [Fact]
    public void SdkSeedHas317NormalNumericEntriesAndNoLegacyEntries()
    {
        var units=Read();Assert.Equal(317,units.NormalByName.Count);Assert.Empty(units.LegacyExact);
        Assert.Equal(0x40730CCCCCCCCCCDul,From(units,Prefix+"millimeters-1.0.1",1.0));
        Assert.Equal(0x3FD381D7DBF487FDul,From(units,Prefix+"meters-1.0.0",1.0));
    }
    [Theory]
    [InlineData("a-b-c","a")] [InlineData("a-2.0.0","a")] [InlineData("a","a")]
    [InlineData("-1.0.0","")] [InlineData("Ӓя\U0001F642-1.0.0","Ӓя\U0001F642")]
    public void NormalIdentityUsesFirstDashUtf16Ordinal(string id,string expected)
        => Assert.Equal(expected,UnitSchemaEvaluator.DisplayNameKey(id));

    [Fact]
    public void ExactSdkRegistrationAndNormalDisplayIdentityRemainSeparate()
    {
        var units=Read((Prefix+"meters-1.0.0","not JSON"));
        Assert.Equal(Prefix+"meters-1.0.0",units.GetNormal(Prefix+"meters-99.9.9")!.FullId);
        Assert.Null(units.GetNormal(Prefix+"Meters-1.0.0"));Assert.Null(units.GetNormal("meters-1.0.0"));
        Assert.Null(units.GetNormal(""));Assert.Null(units.GetNormal(Prefix+"meters\0-1.0.0"));
    }
    [Theory]
    [InlineData("feetFractionalInches-1.0.0","feet-1.0.1")]
    [InlineData("fractionalInches-1.0.0","inches-1.0.1")]
    [InlineData("metersCentimeters-1.0.0","meters-1.0.0")]
    [InlineData("degreesMinutes-1.0.0","degrees-1.0.1")]
    [InlineData("stationingFeet-1.0.0","feet-1.0.1")]
    [InlineData("stationingMeters-1.0.0","meters-1.0.0")]
    [InlineData("stationingSurveyFeet-1.0.0","usSurveyFeet-1.0.0")]
    public void SourceFormatClonesParentNumericFields(string child,string parent)
    {
        var units=Read();var a=units.GetNormal(Prefix+parent)!.Core;var b=units.GetNormal(Prefix+child)!.Core;
        Assert.Equal(a.Dimensions,b.Dimensions);Assert.Equal(a.Absolute,b.Absolute);
        Assert.Equal(a.SiFactor,b.SiFactor);Assert.Equal(a.Offset,b.Offset);Assert.Equal(a.InternalScale,b.InternalScale);
    }

    [Fact]
    public void SlopeCloneClearsDimensionsAndUsesNativeNearVerticalSnapIncludingNegative()
    {
        var units=Read();string id=Prefix+"slopeDegrees-1.0.0";
        Assert.Equal(new int[8],units.GetNormal(id)!.Core.Dimensions);Assert.False(units.GetNormal(id)!.Core.Absolute);
        Assert.Equal(0x4046800000000006ul,From(units,id,1.0));
        Assert.Equal(0x3F91DFBD9410A41Dul,To(units,id,1.0));
        foreach(double value in new[]{90.0,-90.0,270.0,-270.0})Assert.Equal(0x40B66193F0CCDEE8ul,To(units,id,value));
        Assert.Equal(0xBCFD1A62633145C0ul,To(units,id,360.0));
    }
    [Fact]
    public void GallonOverrideIsOriginalReadonlyLiteralNotCoreSiFactor()
    {
        string id=Prefix+"usGallonsPerMinute-1.0.1";
        var core=UnitSchemaEvaluator.EvaluateCore(Map(),out _)![id];var display=Read().GetNormal(id)!.Core;
        Assert.NotEqual(BitConverter.DoubleToUInt64Bits(core.SiFactor),BitConverter.DoubleToUInt64Bits(display.SiFactor));
        Assert.Equal(0x3F1089E961FEAEBAul,BitConverter.DoubleToUInt64Bits(display.SiFactor));
    }
    [Fact]
    public void RatioReciprocalUsesStrictGreaterThanEpsilonBeforeAndAfterLinearMath()
    {
        var units=Read();string id=Prefix+"1ToRatio-1.0.1";
        double epsilon=BitConverter.UInt64BitsToDouble(0x3E112E0BE826D695);
        foreach(double value in new[]{Math.BitDecrement(epsilon),epsilon,-epsilon})
        {
            Assert.Equal(BitConverter.DoubleToUInt64Bits(value),From(units,id,value));
            Assert.Equal(BitConverter.DoubleToUInt64Bits(value),To(units,id,value));
        }
        Assert.Equal(0x41CDCD64FFFFFFFEul,From(units,id,Math.BitIncrement(epsilon)));
        Assert.Equal(BitConverter.DoubleToUInt64Bits(0.5),From(units,id,2.0));
        Assert.Equal(BitConverter.DoubleToUInt64Bits(0.5),To(units,id,2.0));
    }
    [Fact]
    public void UpdateExcludesTeslasAndGramsButDoesNotRemoveSdkGrams()
    {
        string teslas=Prefix+"teslas-1.0.0",grams=Prefix+"grams-2.0.0";
        var units=Read((teslas,Derived(teslas,Prefix+"meters-1.0.0")),(grams,Derived(grams,Prefix+"meters-1.0.0",2)));
        Assert.Null(units.GetNormal(teslas));Assert.Null(units.GetLegacy(teslas));
        Assert.Equal(Prefix+"grams-1.0.1",units.GetNormal(grams)!.FullId);Assert.Empty(units.LegacyExact);
        Assert.False(units.TryFromInternal(1.0,teslas,out _));Assert.False(units.TryToInternal(1.0,teslas,out _));
    }
    [Fact]
    public void OlderVersionHasOwnExactLegacyNumericFieldsAndNormalFallsBack()
    {
        string old=Prefix+"millimeters-1.0.0";var units=Read((old,Derived(old,Prefix+"meters-1.0.0",2)));
        Assert.Equal(Prefix+"millimeters-1.0.1",units.GetNormal(old)!.FullId);Assert.Equal(old,units.GetLegacy(old)!.FullId);
        Assert.Equal(2.0,units.GetLegacy(old)!.Core.SiFactor);Assert.Single(units.LegacyExact);
        Assert.Equal(BitConverter.DoubleToUInt64Bits(1.0/UnitSchemaEvaluator.UniversalInternalScale(2.0,1)),From(units,old,1));
    }
    [Fact]
    public void NewerVersionCopiesCompleteNumericDimensionAndKeepsOldFullLegacy()
    {
        string current=Prefix+"meters-1.0.0",newer=Prefix+"meters-1.0.2";
        var units=Read((newer,Derived(newer,Prefix+"kilograms-1.0.0",2)));
        Assert.Equal(newer,units.GetNormal(current)!.FullId);
        Assert.Equal(new[]{0,0,1,0,0,0,0,0},units.GetNormal(current)!.Core.Dimensions);
        Assert.Equal(current,units.GetLegacy(current)!.FullId);Assert.Equal(1,units.GetLegacy(current)!.Core.Dimensions[0]);
        Assert.Equal(0.5,BitConverter.UInt64BitsToDouble(From(units,newer,1)));
        Assert.Equal(0x3FD381D7DBF487FDul,From(units,current,1));
    }
    [Fact]
    public void ConsecutiveNewerVersionsCopyAbsoluteFlagOwnOffsetAndPreserveBothLegacySnapshots()
    {
        string old=Prefix+"meters-1.0.0",mass=Prefix+"meters-1.0.2",absolute=Prefix+"meters-1.0.3";
        string json=JsonSerializer.Serialize(new{typeid=absolute,inherits=new[]{"autodesk.unit:unit.absolute-1.0.0"},
            constants=new object[]{new{id="parentUnit",typedValue=new{typeid=Prefix+"celsius-1.0.1"}},new{id="offset",value=12.5}}});
        var units=Read((absolute,json),(mass,Derived(mass,Prefix+"kilograms-1.0.0",2)));
        Assert.Equal(absolute,units.GetNormal(old)!.FullId);Assert.True(units.GetNormal(old)!.Core.Absolute);
        Assert.Equal(new[]{0,0,0,0,0,1,0,0},units.GetNormal(old)!.Core.Dimensions);
        Assert.Equal(12.5,units.GetNormal(old)!.Core.Offset);
        Assert.Equal(2,units.LegacyExact.Count);Assert.Equal(2.0,units.GetLegacy(mass)!.Core.SiFactor);
        Assert.False(units.GetLegacy(mass)!.Core.Absolute);Assert.Equal(old,units.GetLegacy(old)!.FullId);
        Assert.Equal(-11.5,BitConverter.UInt64BitsToDouble(From(units,absolute,1)));
    }
    [Theory]
    [InlineData("1.0.1","1.0.0",true)] [InlineData("1.0.0","1.0.1",false)]
    [InlineData("1","1.0",false)] [InlineData("1.0","1",true)]
    [InlineData("1.0.0","1.0.0",false)] [InlineData("4294967295","2147483648",true)]
    public void VersionComparisonIsBoundedUnsignedSourceTokens(string a,string b,bool expected)
        =>Assert.Equal(expected,UnitSchemaEvaluator.NewerDisplayVersion("a-"+a,"a-"+b));

    [Theory]
    [InlineData("1.0.0.1")] [InlineData("1.0.0-beta")] [InlineData("1.a.0")] [InlineData("4294967296")]
    public void UnverifiedVersionCollisionsRejectInsteadOfInventingSemver(string version)
    {
        string id=Prefix+"meters-"+version;
        Assert.Null(UnitSchemaEvaluator.EvaluateDisplay(Map((id,Derived(id,Prefix+"kilograms-1.0.0"))),out string? failure));
        Assert.Contains("Unverified display version",failure);
    }
    [Fact]
    public void NegativeZeroOffsetsAndRejectedNonfiniteValuesAreExplicit()
    {
        var units=Read();double zero=BitConverter.UInt64BitsToDouble(0x8000000000000000);
        Assert.Equal(0x8000000000000000ul,From(units,Prefix+"millimeters-1.0.1",zero));
        Assert.Equal(0ul,To(units,Prefix+"millimeters-1.0.1",zero));
        Assert.Equal(-273.15,BitConverter.UInt64BitsToDouble(From(units,Prefix+"celsius-1.0.1",0)));
        Assert.False(units.TryFromInternal(double.NaN,Prefix+"meters-1.0.0",out _));
        Assert.False(units.TryToInternal(double.PositiveInfinity,Prefix+"meters-1.0.0",out _));
        Assert.False(units.TryToInternal(double.MaxValue,Prefix+"meters-1.0.0",out _));
    }
    [Fact]
    public void RejectedDisplayClearsOldNumbersWithoutLosingValidCoresSiOrRawMaps()
    {
        var state=new ParsingState();using var diag=new StringWriter();
        TfBlockDeserializer.DocumentSchemaPrefix P(TfBlockDeserializer.DocumentSchemaMap units)=>new(0,0,0,units,Map(),[],[],[]);
        RvtFileReader.RecordOwnDocumentSchemas(state,P(Map()),diag);Assert.Equal(317,state.OwnDocumentUnitConversions!.NormalByName.Count);
        string id=Prefix+"meters-1.0.0-beta";
        RvtFileReader.RecordOwnDocumentSchemas(state,P(Map((id,Derived(id,Prefix+"kilograms-1.0.0")))),diag);
        Assert.Null(state.OwnDocumentUnitConversions);Assert.NotNull(state.OwnDocumentUnitCores);
        Assert.NotNull(state.OwnDocumentSiFactors);Assert.NotNull(state.OwnDocumentSchemas);
        Assert.Contains("numeric display unavailable",diag.ToString());
        RvtFileReader.RecordOwnDocumentSchemas(state,null,diag);Assert.Null(state.OwnDocumentUnitConversions);
    }
}
