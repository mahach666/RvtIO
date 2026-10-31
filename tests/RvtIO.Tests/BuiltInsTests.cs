using System.Security.Cryptography;
using System.Text;
using RvtIO.BuiltIns;
using Xunit;

namespace RvtIO.Tests;

public class BuiltInsTests
{
    private static string Digest<T>() where T : struct, Enum
    {
        var sb = new StringBuilder();
        foreach (var f in typeof(T).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            sb.Append(f.Name).Append('=').Append(Convert.ToInt64(f.GetRawConstantValue())).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    // Digests of the "Name=value" lines in declaration order, taken when the enums were generated.
    [Fact]
    public void EnumMembersAndValuesAreUnchanged()
    {
        Assert.Equal(3745, Enum.GetNames<BuiltInParameter>().Length);
        Assert.Equal("334c33bcc652eefbf0720133dcfe1d0dfb9983dfdc18e2f4c34cafccc69929cb", Digest<BuiltInParameter>());
        Assert.Equal(1078, Enum.GetNames<BuiltInCategory>().Length);
        Assert.Equal("531758301de44f299f0a5f487b05c8b1d43544fd6b2275ecd18f48ef54b066d5", Digest<BuiltInCategory>());
        Assert.Equal(117, Enum.GetNames<BuiltInParameterGroup>().Length);
        Assert.Equal("f8e018b3d53e2fd10666c2d320eadbf8db091931d5d5a6456a757f6e1848db56", Digest<BuiltInParameterGroup>());
    }

    [Fact]
    public void GroupLabelsAreUnchanged()
    {
        var sb = new StringBuilder();
        int labeled = 0;
        foreach (var f in typeof(BuiltInParameterGroup).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            var g = (BuiltInParameterGroup)f.GetRawConstantValue()!;
            if (g.ToEnglishLabel(out string label))
            {
                labeled++;
                sb.Append(f.Name).Append('=').Append('"').Append(label).Append('"').Append('\n');
            }
        }
        Assert.Equal(114, labeled);
        Assert.Equal("1b38f7270c632cee76790f5f9aff23854b8953176d272b18477791aa41e87824", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant());
    }

    [Fact]
    public void UndefinedGroupHasNoLabel()
    {
        Assert.False(((BuiltInParameterGroup)123456).ToEnglishLabel(out _));
        Assert.True(BuiltInParameterGroup.PG_FORCES.ToEnglishLabel(out string forces));
        Assert.Equal("Forces", forces);
    }
}
