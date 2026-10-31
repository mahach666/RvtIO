using System.Globalization;
using RvtIO.Parsing;

namespace RvtIO.Compact;

internal static class CompactSolidFacts
{
    private static readonly (string Name, int Id, string Formula)[] Texts =
    [
        ("volume", -1012806, "volume"), ("half", -1012805, "half"), ("maxface", -1012805, "maxface"),
        ("prism", -1004005, "prism"), ("curve", -1004005, ""),
    ];

    internal static void Dump(CompactSolidQuantities quantities, CompactFormatSettings? format, TextWriter output)
    {
        output.Write("id\tcat\tshape\tlevel\tcount\tvolume\tarea\tlargest\tdepth\tcurve\tt_volume\tt_half\tt_maxface\tt_prism\tt_curve\n");
        var ids = quantities.AllSolids.Select(pair => pair.Key).Concat(quantities.AllCurveLengths.Select(pair => pair.Key)).Distinct().Order();
        foreach (int id in ids)
        {
            var solids = quantities.SolidsOf(id);
            double? curve = quantities.CurveLength(id);
            string Cell(double? value) => value is double number ? number.ToString("R", CultureInfo.InvariantCulture) : "";
            var texts = Texts.Select(entry =>
            {
                double? value = entry.Name == "curve" ? curve : solids is { } known ? CompactSolidQuantities.Value(known, entry.Formula) : null;
                if (value is not double number || format == null) return "\\N";
                ApiParameterDefinitions.TryGet(entry.Id, out _, out _, out string spec);
                return CompactValueFormatter.Format(number, spec, format) ?? "\\N";
            });
            output.Write(string.Create(CultureInfo.InvariantCulture,
                $"{id}\t{solids?.Category}\t{solids?.Shape}\t{solids?.Level}\t{solids?.Count}\t{Cell(solids?.Volume)}\t{Cell(solids?.Area)}\t{Cell(solids?.LargestFace)}\t{Cell(solids?.PrismDepth)}\t{Cell(curve)}\t{string.Join("\t", texts)}\n"));
        }
    }
}
