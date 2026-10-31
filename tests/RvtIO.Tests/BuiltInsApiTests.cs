using System.Security.Cryptography;
using System.Text;
using RvtIO.BuiltIns;
using Xunit;

namespace RvtIO.Tests;

public class BuiltInsApiTests
{
    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string Digest<T>() where T : struct, Enum
    {
        var sb = new StringBuilder();
        foreach (var f in typeof(T).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            sb.Append(f.Name).Append('=').Append(Convert.ToInt64(f.GetRawConstantValue())).Append('\n');
        return Hash(sb.ToString());
    }

    // Digests of the "Name=value" lines in declaration order, taken from the original enums.
    [Theory]
    [InlineData(typeof(DisplayUnitType), 255, "7f6edabcc964272951f7416f723730f5d852a316bbaa911dc216aa7d7f58a9d7")]
    [InlineData(typeof(UnitType), 135, "3a71b838f5736c01b0be06e801fbb6daaee00c30415c614c93900edc341bdbdb")]
    [InlineData(typeof(UnitGroup), 7, "830f7b74b0ff2b6f392f14549039bf87f5005da9c2aaf3b8bc84bb329b842b18")]
    [InlineData(typeof(ParameterType), 137, "17a06a6e6e523fe7ffa664cae4f127d44dc0a8e89b06b1e64914c325b5df91fd")]
    [InlineData(typeof(UnitSymbolType), 318, "6bedd7fd61cb01aec4d20a39e9ed94292b7af17a858ac25de40ea1ffed1ca965")]
    [InlineData(typeof(MEPSystemClassification), 33, "b37b467f481013cdf44047559f9ba8740710bd3fae5362195af7d179e8b0de90")]
    [InlineData(typeof(FlowDirectionType), 3, "621748ece58c9aa7542c1de0f34810fff7360514e1cbd3a956dbd3f995178bf2")]
    [InlineData(typeof(PipeFlowConfigurationType), 4, "c9624f42e9e55b570b5503c4d420a00cf9933a3e142fb7df8c6c9593df8c2268")]
    public void UnitEnumMembersAndValuesAreUnchanged(Type type, int count, string digest)
    {
        Assert.Equal(count, Enum.GetNames(type).Length);
        var m = typeof(BuiltInsApiTests).GetMethod(nameof(Digest), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.Equal(digest, (string)m.MakeGenericMethod(type).Invoke(null, null)!);
    }

    // Every helper over all defined values, undefined values, case variants and unknown text,
    // hashed; the expected hash comes from the original implementation.
    [Fact]
    public void LookupHelpersBehaveAsBefore()
    {
        var table = Table();
        Assert.Equal(8780, table.Count(c => c == '\n'));
        Assert.Equal("1170dd1bf507cb3ebf02d0806be2735a8915064458db6cbee73974714224c4df", Hash(table));
    }

    [Fact]
    public void LookupHelperSamples()
    {
        Assert.True(BuiltInCategory.OST_Walls.TryGetCategoryName(out var wall));
        Assert.Equal("Walls", wall);
        Assert.False(((BuiltInCategory)123456).TryGetCategoryName(out _));

        Assert.True(UnitType.UT_Length.TryGetCatalogString(out var length));
        Assert.Equal("LENGTH", length);
        Assert.True("length".TryGetUnitTypeFromCatalogString(out var unitType));
        // Several unit types share the catalog string; the first one in the table wins.
        Assert.Equal(UnitType.UT_SheetLength, unitType);
        Assert.False("nope".TryGetUnitTypeFromCatalogString(out unitType));
        Assert.Equal(UnitType.UT_Undefined, unitType);

        Assert.True(UnitSymbolType.UST_MM.TryGetSymbol(out var mm));
        Assert.Equal("mm", mm);
        Assert.True("mm".TryGetFromSymbol(out var symbol));
        Assert.Equal(UnitSymbolType.UST_MM, symbol);
        Assert.False("MM".TryGetFromSymbol(out symbol));
        Assert.Equal(UnitSymbolType.UST_NONE, symbol);
        Assert.True("mm".TryGetFromUnitSymbol(out DisplayUnitType display));
        Assert.Equal(DisplayUnitType.DUT_MILLIMETERS, display);

        Assert.Equal("LENGTH", ParameterType.Length.ToSharedDataType());
        Assert.Equal(ParameterType.Length, "length".FromSharedDataType());
        Assert.Equal(ParameterType.Invalid, "nope".FromSharedDataType());
        Assert.Null(((ParameterType)99999).ToSharedDataType());

        Assert.True(DisplayUnitType.DUT_MILLIMETERS.TryGetCatalogString(out var catalog));
        Assert.Equal("MILLIMETERS", catalog);
        Assert.True("millimeters".TryGetFromCatalogString(out display));
        Assert.Equal(DisplayUnitType.DUT_MILLIMETERS, display);

        Assert.True("Forces".FromEnglishLabel(out var group));
        Assert.Equal(BuiltInParameterGroup.PG_FORCES, group);
        // Two groups are labeled "Supports"; the first one declared in the table wins.
        Assert.True("Supports".FromEnglishLabel(out group));
        Assert.Equal(BuiltInParameterGroup.PG_STAIRS_SUPPORTS, group);
        Assert.False("forces".FromEnglishLabel(out group));
        Assert.Equal(BuiltInParameterGroup.INVALID, group);
    }

    static string L<T>(IEnumerable<T>? l) => l == null ? "null" : string.Join(",", l.Select(x => Convert.ToInt32(x)));
    static string R(bool ok, string? v) => ok + "|" + (v ?? "null");
    static string I(object e) => Convert.ToInt32(e).ToString();

    static IEnumerable<int> Ints<T>() where T : struct, Enum =>
        Enum.GetValues<T>().Select(v => Convert.ToInt32(v)).Concat([int.MinValue, -1000, -3, 99999, 123456, int.MaxValue]).Distinct().ToArray();

    // Case variants, padding and unknown text for every known string.
    static IEnumerable<string?> Texts(IEnumerable<string> known) =>
        known.SelectMany(s => new[] { s, s.ToUpperInvariant(), s.ToLowerInvariant(), s + " ", " " + s })
            .Concat([null, "", "garbage", "Ft", "M", "m", "T", "t", "ST", "st", "mm", "MM"]).Distinct().ToArray();

    // One line per call: "name input: result". Each helper is run on all defined values plus undefined ones.
    internal static string Table()
    {
        var sb = new StringBuilder();
        void Ints1<T>(string name, Func<int, string> f) where T : struct, Enum
        { foreach (var v in Ints<T>()) sb.Append(name).Append(' ').Append(v).Append(": ").Append(f(v)).Append('\n'); }
        void Strs(string name, IEnumerable<string> known, Func<string?, string> f)
        { foreach (var s in Texts(known)) sb.Append(name).Append(' ').Append(s ?? "<null>").Append(": ").Append(f(s)).Append('\n'); }
        IEnumerable<string> Known<T>(Func<int, string> f) where T : struct, Enum =>
            Ints<T>().Select(f).Where(r => r.StartsWith("True|")).Select(r => r.Substring(5)).ToArray();

        Func<int, string> cat = v => { var a = ((BuiltInCategory)v).TryGetCategoryName(out var s); return R(a, s); };
        Func<int, string> grp = v => { var a = ((BuiltInParameterGroup)v).ToEnglishLabel(out var s); return R(a, s); };
        Func<int, string> utCat = v => { var a = ((UnitType)v).TryGetCatalogString(out var s); return R(a, s); };
        Func<int, string> ustSym = v => { var a = ((UnitSymbolType)v).TryGetSymbol(out var s); return R(a, s); };
        Func<int, string> ptShared = v => ((ParameterType)v).ToSharedDataType() ?? "null";
        Func<int, string> dutCat = v => { var a = ((DisplayUnitType)v).TryGetCatalogString(out var s); return R(a, s); };

        Ints1<BuiltInCategory>("category", cat);
        Ints1<BuiltInParameterGroup>("group", grp);
        Strs("group-from-label", Known<BuiltInParameterGroup>(grp), s => { var a = s!.FromEnglishLabel(out BuiltInParameterGroup g); return R(a, I(g)); });
        Ints1<UnitType>("unit-catalog", utCat);
        Ints1<UnitType>("unit-group", v => { var a = ((UnitType)v).TryGetGroup(out var g); return R(a, I(g)); });
        Ints1<UnitType>("unit-parameter", v => { var a = ((UnitType)v).TryGetParameterType(out var g); return R(a, I(g)); });
        Ints1<UnitType>("unit-displays", v => { var a = ((UnitType)v).TryGetDisplayForUnitType(out var d); return a + L(d); });
        Strs("unit-from-catalog", Known<UnitType>(utCat), s => { var a = s!.TryGetUnitTypeFromCatalogString(out UnitType u); return R(a, I(u)); });
        Ints1<UnitSymbolType>("symbol-text", ustSym);
        Ints1<UnitSymbolType>("symbol-display", v => { var a = ((UnitSymbolType)v).TryGetFromUnitSymbol(out DisplayUnitType d); return R(a, I(d)); });
        var symbols = Known<UnitSymbolType>(ustSym).ToArray();
        Strs("symbol-from-text", symbols, s => { var a = s!.TryGetFromSymbol(out UnitSymbolType u); return R(a, I(u)); });
        Strs("text-display", symbols, s => { var a = s!.TryGetFromUnitSymbol(out DisplayUnitType d); return R(a, I(d)); });
        Ints1<ParameterType>("parameter-shared", ptShared);
        Ints1<ParameterType>("parameter-unit", v => I(((ParameterType)v).GetUnitType()));
        Ints1<ParameterType>("parameter-displays", v => { var a = ((ParameterType)v).TryGetDisplayForParameterType(out var d); return a + L(d); });
        Strs("parameter-from-shared", Ints<ParameterType>().Select(ptShared).Where(x => x != "null").ToArray(), s => I(s!.FromSharedDataType()));
        Ints1<DisplayUnitType>("display-catalog", dutCat);
        Ints1<DisplayUnitType>("display-units", v => { var a = ((DisplayUnitType)v).TryGetUnitType(out var l); return a + L(l); });
        Ints1<DisplayUnitType>("display-symbols", v => { var a = ((DisplayUnitType)v).TryGetUnitSymbolType(out var l); return a + L(l); });
        Strs("display-from-catalog", Known<DisplayUnitType>(dutCat), s => { var a = s!.TryGetFromCatalogString(out DisplayUnitType d); return R(a, I(d)); });
        return sb.ToString();
    }

}


