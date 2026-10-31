using System.Text.Json;
using RvtIO.Model;
using RvtIO.Parsing;
using RvtIO.RootProbe;
using Xunit;

namespace RvtIO.Tests;

public class RootProbeParameterInputTests
{
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    [Theory]
    [InlineData(StorageType.Integer, 17)]
    [InlineData(StorageType.ElementId, -1)]
    [InlineData(StorageType.Double, 0.125)]
    [InlineData(StorageType.String, "  own 'value'  ")]
    public void MaterializedProjectionPreservesTypedValueAndAllPriorFields(StorageType storage, object value)
    {
        var definition = new Definition { Id = 321, Name = "Manufacturer" };
        var parameter = new Parameter { Id = 321, Value = value, StorageType = storage,
            Definition = definition, IsApproximate = true, Guid = Guid.Parse("a3083598-f17e-40f5-beb7-3a1d58be3bfe") };
        string before = JsonSerializer.Serialize(parameter);
        var actual = Json(ParameterInputProbe.Materialized(parameter));
        Assert.Equal(321, actual.GetProperty("Id").GetInt32());
        Assert.Equal(Json(value).GetRawText(), actual.GetProperty("Value").GetRawText());
        Assert.Equal((int)storage, actual.GetProperty("StorageType").GetInt32());
        Assert.True(actual.GetProperty("IsApproximate").GetBoolean());
        Assert.Equal(Json(definition).GetRawText(), actual.GetProperty("Definition").GetRawText());
        Assert.Equal(parameter.Guid, actual.GetProperty("Guid").GetGuid());
        Assert.Equal(before, JsonSerializer.Serialize(parameter));
        Assert.Same(definition, parameter.Definition);
    }

    [Theory]
    [InlineData("autodesk.spec.aec:length-2.0.0", "mm", 304.8)]
    [InlineData("autodesk.spec.aec:heatTransferCoefficient-2.0.0", "W/(m²·K)", 1.0)]
    [InlineData(null, null, null)]
    public void MaterializedProjectionReportsExistingUnitMetadataWithoutInferringIt(
        string? spec, string? symbol, double? factor)
    {
        var parameter = new Parameter { Id = -1002206, Value = null, StorageType = StorageType.Double,
            SpecType = spec, UnitSymbol = symbol, ConversionFactor = factor };
        var actual = Json(ParameterInputProbe.Materialized(parameter));
        Assert.Equal(spec, actual.GetProperty("SpecType").GetString());
        Assert.Equal(symbol, actual.GetProperty("UnitSymbol").GetString());
        Assert.Equal(factor, actual.GetProperty("ConversionFactor").ValueKind == JsonValueKind.Null
            ? null : actual.GetProperty("ConversionFactor").GetDouble());
        Assert.Equal(JsonValueKind.Null, actual.GetProperty("Value").ValueKind);
        Assert.Equal(spec, parameter.SpecType);
        Assert.Equal(symbol, parameter.UnitSymbol);
        Assert.Equal(factor, parameter.ConversionFactor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnRootProjectionPreservesEveryTypedStoreIncludingDuplicatesAndCompleteness(bool complete)
    {
        var root = new TfBlockDeserializer.NativeParams { RootParameterStoresComplete = complete };
        root.Fields.Add("m_id", 41);
        root.Ints.Add((-13, 1)); root.Ints.Add((-13, 0));
        root.ElementIds.Add((-14, -1));
        root.Strings.Add((-15, "")); root.Strings.Add((-15, " x "));
        root.Doubles.Add((-16, 0.125)); root.Doubles.Add((-16, -0.25));
        var actual = Json(ParameterInputProbe.OwnRoot(714, root));
        Assert.Equal(714, actual.GetProperty("classId").GetInt32());
        Assert.Equal(41, actual.GetProperty("fields").GetProperty("m_id").GetInt32());
        Assert.Equal("[[-13,1],[-13,0]]", actual.GetProperty("ints").GetRawText());
        Assert.Equal("[[-14,-1]]", actual.GetProperty("elementIds").GetRawText());
        Assert.Equal("[[-15,\"\"],[-15,\" x \"]]", actual.GetProperty("strings").GetRawText());
        Assert.Equal("[[-16,0.125],[-16,-0.25]]", actual.GetProperty("doubles").GetRawText());
        Assert.Equal(complete, actual.GetProperty("rootParameterStoresComplete").GetBoolean());
        Assert.Equal(2, root.Ints.Count); Assert.Equal(2, root.Doubles.Count);
        Assert.Equal(2, root.Strings.Count); Assert.Single(root.ElementIds);
    }

    [Fact]
    public void UndecodableRootStaysNullRatherThanAnEmptySuccessfulStore()
    {
        var actual = Json(ParameterInputProbe.OwnRoot(1915, null));
        Assert.Equal(1915, actual.GetProperty("classId").GetInt32());
        foreach (string name in new[] { "fields", "elementIds", "ints", "strings", "doubles", "rootParameterStoresComplete" })
            Assert.Equal(JsonValueKind.Null, actual.GetProperty(name).ValueKind);
    }

    [Fact]
    public void KnownEmptyCompleteRootIsDistinctFromAnUndecodableOne()
    {
        var actual = Json(ParameterInputProbe.OwnRoot(714,
            new TfBlockDeserializer.NativeParams { RootParameterStoresComplete = true }));
        Assert.True(actual.GetProperty("rootParameterStoresComplete").GetBoolean());
        Assert.Equal("{}", actual.GetProperty("fields").GetRawText());
        foreach (string name in new[] { "elementIds", "ints", "strings", "doubles" })
            Assert.Equal("[]", actual.GetProperty(name).GetRawText());
    }

    [Fact]
    public void NonfiniteOwnDoubleFailsSerializationRatherThanLosingTheRecord()
    {
        var root = new TfBlockDeserializer.NativeParams();
        root.Doubles.Add((-16, double.NaN));
        Assert.Throws<ArgumentException>(() => Json(ParameterInputProbe.OwnRoot(714, root)));
        Assert.Single(root.Doubles);
    }
}
