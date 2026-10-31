using RvtIO.Ifc;
using Xunit;

namespace RvtIO.Tests;

public class IfcCurrentStringPropertyByteLimitTests
{
    [Theory]
    [InlineData("IFCLABEL")]
    [InlineData("IFCIDENTIFIER")]
    public void LimitCountsUtf8BytesBeforeStepEscaping(string kind)
    {
        (string Input, string Expected)[] cases =
        [
            ("", ""), (new('a', 254), new('a', 254)), (new('a', 255), new('a', 255)),
            (new('a', 256), new('a', 255)),
            ("x" + new string('Б', 130), "x" + new string('Б', 127)),
            (new('€', 86), new('€', 85)),
            ("abc" + string.Concat(Enumerable.Repeat("\U0001F600", 64)), "abc" + string.Concat(Enumerable.Repeat("\U0001F600", 63))),
            (new string('a', 250) + "'\\,xyzzz", new string('a', 250) + "'\\,xy"),
        ];
        foreach (var (input, expected) in cases)
        {
            var writer = new StepWriter();
            writer.Add("IFCPROPERTYSINGLEVALUE", $"'Reference','kept',{kind}({StepWriter.Str(input)}),#7");
            Assert.Equal(input == expected ? 0 : 1, IfcExporter.ApplyCurrentStringPropertyByteLimits(writer));
            Assert.Equal($"'Reference','kept',{kind}({StepWriter.Str(expected)}),#7", writer.EntityAt(0).Args);
            Assert.Equal(0, IfcExporter.ApplyCurrentStringPropertyByteLimits(writer));
        }
    }

    [Fact]
    public void SampleFloorReferenceUsesTheNative255BytePrefix()
    {
        const string input = "16_ТЕС_Холодная асфальтовая мастика(0)_Подсыпка из песка(37)_ Стяжка -бетон класса В15 армированный фиброволокном ВСМ-12 (40-80))Гидроизоляция(0)(Керамическая плитка на ц.п. р-ре(30)_110-150";
        const string expected = "16_ТЕС_Холодная асфальтовая мастика(0)_Подсыпка из песка(37)_ Стяжка -бетон класса В15 армированный фиброволокном ВСМ-12 (40-80))Гидроизоляция(0)(Кер";
        var writer = new StepWriter();
        writer.Add("IFCPROPERTYSINGLEVALUE", $"'Reference',$,IFCIDENTIFIER({StepWriter.Str(input)}),$");
        Assert.Equal(1, IfcExporter.ApplyCurrentStringPropertyByteLimits(writer));
        Assert.Equal($"'Reference',$,IFCIDENTIFIER({StepWriter.Str(expected)}),$", writer.EntityAt(0).Args);
    }

    [Fact]
    public void TextProductNamesAndPropertyMetadataStayUnchanged()
    {
        var writer = new StepWriter(); string text = StepWriter.Str(new string('x', 300));
        string[] arguments = [$"{text},$,IFCTEXT({text}),$", $"{text},$,{text},$", $"{text},$,(IFCLABEL({text})),$"];
        foreach (string args in arguments) writer.Add("IFCPROPERTYSINGLEVALUE", args);
        writer.Add("IFCPROPERTYLISTVALUE", $"'List',$,(IFCLABEL({text})),$");
        writer.Add("IFCCOLOURRGB", $"{text},0.,0.,0.");
        Assert.Equal(0, IfcExporter.ApplyCurrentStringPropertyByteLimits(writer));
        for (int i = 0; i < arguments.Length; i++) Assert.Equal(arguments[i], writer.EntityAt(i).Args);
        Assert.Equal($"'List',$,(IFCLABEL({text})),$", writer.EntityAt(3).Args);
        Assert.Equal($"{text},0.,0.,0.", writer.EntityAt(4).Args);
    }

    [Fact]
    public void SplitUtf8AndMalformedLiteralsKeepTheirExistingValue()
    {
        var writer = new StepWriter();
        string[] values = [new string('Б', 128), new string('a', 254) + "€", new string('a', 253) + "\U0001F600", new string('a', 256) + '\ud800'];
        var originals = values.Select(text => $"'Reference',$,IFCLABEL({StepWriter.Str(text)}),$")
            .Concat(["'Reference',$,IFCLABEL('bad'quote'),$", "'Reference',$,IFCLABEL($),$", "'Reference',$,IFCLABEL('x'),$,extra"]).ToArray();
        foreach (string args in originals) writer.Add("IFCPROPERTYSINGLEVALUE", args);
        Assert.Equal(0, IfcExporter.ApplyCurrentStringPropertyByteLimits(writer));
        for (int i = 0; i < originals.Length; i++) Assert.Equal(originals[i], writer.EntityAt(i).Args);
    }

    [Fact]
    public void NativeCacheRetainsDistinctOriginalStringsEvenWhenPrefixesMatch()
    {
        var writer = new StepWriter(); string prefix = new('a', 255);
        writer.Add("IFCPROPERTYSINGLEVALUE", $"'Reference',$,IFCLABEL('{prefix}X'),$");
        writer.Add("IFCPROPERTYSINGLEVALUE", $"'Reference',$,IFCLABEL('{prefix}Y'),$");
        writer.Add("IFCPROPERTYSET", "'0000000000000000000000',#9,'A',$,(#1)");
        writer.Add("IFCPROPERTYSET", "'0000000000000000000001',#9,'B',$,(#2)");
        Assert.Equal(0, IfcExporter.ApplyCurrentPropertyValueCache(writer));
        Assert.Equal(2, IfcExporter.ApplyCurrentStringPropertyByteLimits(writer));
        Assert.Equal(4, writer.EntityCount);
        Assert.Equal(writer.EntityAt(0).Args, writer.EntityAt(1).Args);
        Assert.EndsWith("(#1)", writer.EntityAt(2).Args); Assert.EndsWith("(#2)", writer.EntityAt(3).Args);
    }
}
