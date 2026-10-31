using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;
public class IfcDesignOptionFilterTests
{
    private static List<string> ExportedTags(RevitFileInfo info)
    {
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-option-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter().Export(info, path);
            return Regex.Matches(File.ReadAllText(path), @"#\d+=IFCCOVERING\([^\r\n]*,'(\d+)',\.[A-Z]+\.\);").Select(m => m.Groups[1].Value).ToList();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static Element Covering(int id, int? option)
    {
        var covering = IfcExporterCoveringCommonPropertySetTests.Product(id, 200, -2000038, "OST_Ceilings", "Native covering");
        covering.NativeHeaderDesignOptionId = option;
        return covering;
    }

    private static RevitFileInfo Info(int?[] options)
    {
        var info = new RevitFileInfo();
        info.Elements.Add(new Element { Id = 875634, NativePrimaryDesignOptionId = 875635 });
        info.Elements.Add(new Element { Id = 875635, NativeDesignOptionSetId = 875634 });
        info.Elements.Add(new Element { Id = 875638, NativeDesignOptionSetId = 875634 });
        for (int index = 0; index < options.Length; index++) info.Elements.Add(Covering(100 + index, options[index]));
        return info;
    }

    [Fact]
    public void NonPrimaryOptionElements_AreNotExported_AndTheModelKeepsThem()
    {
        var info = Info([-1, 875635, 875638]);
        int count = info.Elements.Count;
        Assert.Equal(["100", "101"], ExportedTags(info));
        Assert.Equal(count, info.Elements.Count);
        Assert.Contains(info.Elements, element => element.Id == 102);
    }

    [Fact]
    public void UnresolvedOption_FailsClosedAndKeepsTheElement()
        => Assert.Equal(["100", "101"], ExportedTags(Info([null, 777])));
}
