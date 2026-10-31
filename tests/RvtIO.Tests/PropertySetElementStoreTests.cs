using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>A PropertySetElement (class 3073, an asset of a material) keeps its four parameter stores in the ParamSet
/// object it points to, so the stores follow that object in the deferred queue. The block is the thermal asset
/// "thermal:solid" (Concrete, stone aggregate) of Test_2022.rvt, element 174516.</summary>
public class PropertySetElementStoreTests
{
    private const ushort PropertySetElementClassId = 3073;

    private const string ThermalAssetBlock =
        "B4A9020037C0DE101B020000010C000000000000000000000000000000000000000000000000000000000000000001000000B4A90200FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF000000FFFFFFFF400B02000000FFFFFFFF470BFFFFFFFF490BFFFFFFFF460BFFFFFFFF480B0B000000CF9BC555DFD69041B66AEEFF0000000000000000B76AEEFFFCA9F1D24D62503FB86AEEFFC2DA8AB45F90CE3DB96AEEFF666666666666EE3FC06AEEFF9721BC643D485040C76AEEFF2F39ED9EE39FBB40C86AEEFFDD43F7D03D740B40C96AEEFFDD43F7D03D740B40CA6AEEFFDD43F7D03D740B40CB6AEEFFDD43F7D03D740B40CC6AEEFF04000000AE6AEEFF00000000BA6AEEFF000000000072EEFF030000009E99EEFF0000000007000000AA6AEEFF0D00000074006800650072006D0061006C003A0073006F006C0069006400AB6AEEFF00000000AC6AEEFF080000004100750074006F006400650073006B00AF6AEEFF24000000300035004100420038004300300031002D0039004500310035002D0034004400310039002D0038003700390031002D00350043004400390038003200460037003100350046003400EF71EEFF1900000043006F006E00630072006500740065002C002000730074006F006E0065002000610067006700720065006700610074006500FE71EEFF0800000043006F006E0063007200650074006500FF71EEFF0800000043006F006E0063007200650074006500000000001B020000";

    [Fact]
    public void ThermalAssetStores_AreDecoded()
    {
        byte[] block = Convert.FromHexString(ThermalAssetBlock);
        var parameters = TfBlockDeserializer.ExtractParams(block, 0, 539, PropertySetElementClassId);

        Assert.NotNull(parameters);
        var doubles = parameters!.Doubles.ToDictionary(pair => pair.Item1, pair => pair.Item2);
        Assert.Equal(11, doubles.Count);
        // THERMAL_MATERIAL_PARAM_EMISSIVITY and PHY_MATERIAL_PARAM_THERMAL_CONDUCTIVITY (stored as W/(m K) / 0.3048).
        Assert.Equal(0.95, doubles[-1152320], 12);
        Assert.Equal(1.046, doubles[-1152308] * 0.3048, 9);
        Assert.Equal(doubles[-1152308], doubles[-1152309]);
        Assert.Contains(parameters.Strings, pair => pair.Item1 == -1152342 && pair.Item2 == "thermal:solid");
        Assert.Contains(parameters.Ints, pair => pair.Item1 == -1150464 && pair.Item2 == 3);
    }

    [Fact]
    public void BlockWithAWrongFooter_IsRejected()
    {
        byte[] block = Convert.FromHexString(ThermalAssetBlock);
        block[^4] ^= 0xFF;
        Assert.Null(TfBlockDeserializer.ExtractParams(block, 0, 539, PropertySetElementClassId));
    }
}
