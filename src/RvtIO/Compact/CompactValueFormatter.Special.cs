using System.Globalization;
using RvtIO.Parsing;

namespace RvtIO.Compact;

internal static partial class CompactValueFormatter
{
    private const string DegreesMinutes = UnitPrefix + "degreesMinutes-1.0.0";
    private const string StationingFeet = UnitPrefix + "stationingFeet-1.0.0";
    private const string StationingMeters = UnitPrefix + "stationingMeters-1.0.0";
    private const string StationingSurveyFeet = UnitPrefix + "stationingSurveyFeet-1.0.0";
    private const string DegreeMark = "autodesk.unit.symbol:degree-1.0.1";

    /// <summary>Degrees minutes seconds (57 deg 17' 45") and stationing (0+000.30).</summary>
    private static string? FormatSpecial(Layout layout, CompactFormatSettings settings)
        => layout.Unit switch
        {
            DegreesMinutes => FormatDegreesMinutes(layout, settings),
            StationingFeet or StationingMeters or StationingSurveyFeet => FormatStationing(layout),
            _ => null,
        };

    private static string? FormatDegreesMinutes(Layout layout, CompactFormatSettings settings)
    {
        double rounded = RoundToAccuracy(layout.Value, layout.Accuracy, layout.Rounding);
        bool negative = SignThreshold > rounded;
        if (negative) rounded = -rounded;
        double degreesValue = rounded + 6.944444444444444e-13;
        long degrees = Truncate64(degreesValue);
        double minutesValue = (degreesValue - degrees) * 60.0;
        long minutes = Truncate64(minutesValue);
        double seconds = (minutesValue - minutes) * 60.0;
        int places = CountPlaces(layout.Accuracy * 3600.0, 10.0, 12);
        if (layout.SuppressTrailingZeros) places = CountPlaces(rounded * 3600.0, 10.0, places);
        int width = places > 0 ? places + 3 : 2;
        if (Label(settings, DegreeMark) is not { } degreeMark || Label(settings, FootMark) is not { } minuteMark
            || Label(settings, InchMark) is not { } secondMark) return null;
        string secondsText = CompactFixedNumber.Format(seconds, places);
        if (layout.Decimal != '.') secondsText = secondsText.Replace('.', layout.Decimal);
        if (secondsText.Length < width) secondsText = new string('0', width - secondsText.Length) + secondsText;
        string body = degrees.ToString(CultureInfo.InvariantCulture) + degreeMark
            + " " + minutes.ToString("00", CultureInfo.InvariantCulture) + minuteMark
            + " " + secondsText + secondMark;
        return Sign(body, negative, layout);
    }

    private static string FormatStationing(Layout layout)
    {
        double rounded = RoundToAccuracy(Math.Abs(layout.Value), layout.Accuracy, layout.Rounding);
        double station = layout.Unit == StationingMeters ? 1000.0 : 100.0;
        string whole = Truncate64(rounded / station).ToString(CultureInfo.InvariantCulture);
        if (layout.Grouping) whole = Group(whole, new Layout { Group = layout.Group, GroupAmount = layout.GroupAmount });
        if (SignThreshold > layout.Value) whole = "-" + whole;
        int places = CountPlaces(layout.Accuracy, 10.0, 12);
        if (layout.SuppressTrailingZeros) places = CountPlaces(rounded, 10.0, places);
        int width = (int)Math.Log10(station);
        if (places != 0) width += 1 + places;
        string rest = CompactFixedNumber.Format(rounded % station, places);
        if (layout.Decimal != '.') rest = rest.Replace('.', layout.Decimal);
        if (rest.Length < width) rest = new string('0', width - rest.Length) + rest;
        return whole + "+" + rest;
    }

    private static string? FormatRise(Layout layout, CompactFormatSettings settings)
    {
        string unit; double run; bool minimalRun = false;
        switch (layout.Unit)
        {
            case RiseDividedBy12Inches: unit = FractionalInches; run = 12.0; break;
            case RiseDividedBy120Inches: unit = FractionalInches; run = 120.0; break;
            case RiseDividedBy1Foot: unit = FeetFractionalInches; run = 1.0; break;
            case RiseDividedBy10Feet: unit = FeetFractionalInches; run = 10.0; minimalRun = true; break;
            case RiseDividedBy1000Millimeters: unit = Millimeters; run = 1000.0; minimalRun = true; break;
            default: return null;
        }
        if (!(layout.Accuracy > 0 && layout.Accuracy <= 1e12)) return null;
        double accuracy;
        if (unit == FractionalInches) accuracy = Math.Max(1.0, Math.Floor(layout.Accuracy * 1048576.0 + 0.5)) / 1048576.0;
        else if (unit == FeetFractionalInches) accuracy = Math.Max(1.0, Math.Floor(layout.Accuracy * 12.0 * 1048576.0 + 0.5)) / 1048576.0 / 12.0;
        else
        {
            double steps = Math.Floor(layout.Accuracy * 1e12 + 0.5);
            if (steps < 1) return null;
            accuracy = steps * 1e-12;
        }
        if (!TryConvertToInternal(layout.Value, unit, settings.Conversions, out double riseInternal)
            || !TryConvertToInternal(run, unit, settings.Conversions, out double runInternal)) return null;
        bool feetInches = unit == FeetFractionalInches;
        var inner = new TfBlockDeserializer.UnitFormatOption("autodesk.spec.aec:length-2.0.1",
            unit == FractionalInches ? InchMark : "", unit, accuracy, layout.Rounding,
            (byte)(feetInches && layout.SuppressLeadingZeros ? 1 : 0), (byte)(feetInches && layout.SuppressSpaces ? 1 : 0),
            0, 0, (byte)(layout.Grouping ? 1 : 0), 0);
        string? first = Format(riseInternal, inner, settings, layout.Editing, minimalDisplay: false);
        string? second = Format(runInternal, inner, settings, layout.Editing, minimalDisplay: minimalRun);
        return first == null || second == null ? null : first + " / " + second;
    }
}
