using System.Globalization;
using System.Text;
using RvtIO.Parsing;

namespace RvtIO.Compact;

internal static partial class CompactValueFormatter
{
    private const string FootMark = "autodesk.unit.symbol:footSingleQuote-1.0.1";
    private const string InchMark = "autodesk.unit.symbol:inchDoubleQuote-1.0.1";
    private const double MaxCount = 2147483647.0;

    /// <summary>Feet and fractional inches (27' - 2 5/16") or fractional inches (3/32").</summary>
    private static string? FormatFractional(Layout layout, CompactFormatSettings settings)
    {
        bool inchesOnly = layout.Unit == FractionalInches;
        double value = layout.Value, accuracy = layout.Accuracy;
        if (inchesOnly) { value /= 12.0; accuracy /= 12.0; }
        double rounded = RoundToAccuracy(Math.Max(Math.Min(value, MaxCount), -MaxCount), accuracy, layout.Rounding);
        bool negative = SignThreshold > rounded;
        if (negative) rounded = -rounded;
        double accuracyInches = accuracy * 12.0;
        int denominator = 1 << CountPlaces(accuracyInches, 2.0, 20);
        // Half of the finest fraction (2^-21 inch) nudges the value over a rounding edge.
        double feetValue = Math.Pow(2.0, -20.0) * 0.5 / 12.0 + rounded;
        long feet = inchesOnly ? 0 : Truncate64(feetValue);
        double inchesValue = Math.Max(Math.Min((feetValue - feet) * 12.0, MaxCount), -MaxCount);
        long inches = Truncate64(inchesValue);
        long numerator = Truncate64((inchesValue - inches) * denominator);
        long reducedDenominator = denominator;
        if (layout.SuppressTrailingZeros)
        {
            while (numerator != 0 && (numerator & 1) == 0) { numerator /= 2; reducedDenominator /= 2; }
        }

        string feetText = feet.ToString(CultureInfo.InvariantCulture);
        string inchText = inches.ToString(CultureInfo.InvariantCulture);
        if (layout.Grouping)
        {
            feetText = Group(feetText, layout);
            inchText = Group(inchText, layout);
        }
        string fractionText = numerator.ToString(CultureInfo.InvariantCulture) + "/" + reducedDenominator.ToString(CultureInfo.InvariantCulture);

        // Which parts are written: the feet, the whole inches and the fraction. A part is dropped when it is
        // zero and the options suppress leading zeros (or the accuracy is whole feet / has no fraction).
        bool hideLeading = !layout.Editing && (layout.SuppressLeadingZeros || layout.MinimalDisplay);
        bool leadingOrMinimal = layout.SuppressLeadingZeros || layout.MinimalDisplay || inchesOnly;
        bool wholeFeetAccuracy = !inchesOnly && denominator == 1
            && Truncate32(accuracyInches + 0.5) % 12 == 0;
        bool minimalOrWhole = wholeFeetAccuracy || layout.MinimalDisplay;
        bool showFraction = !(denominator == 1 || numerator == 0 && layout.SuppressTrailingZeros);
        bool showFeet = true, showInches = false;
        if (!wholeFeetAccuracy || showFraction)
        {
            if (inchesOnly || feet == 0 && hideLeading) showFeet = false;
            showInches = !wholeFeetAccuracy;
            if (!wholeFeetAccuracy && (showFeet || showFraction) && inches == 0
                && (!showFeet && leadingOrMinimal || !showFraction && minimalOrWhole))
                showInches = false;
        }

        var text = new StringBuilder();
        if (showFeet)
        {
            if (Label(settings, FootMark) is not { } footMark) return null;
            text.Append(feetText).Append(footMark);
            if (showInches || showFraction)
                text.Append(layout.Editing ? "  " : layout.SuppressSpaces ? "-" : " - ");
        }
        if (showInches)
        {
            text.Append(inchText);
            if (showFraction) text.Append(' ').Append(fractionText);
        }
        else if (showFraction) text.Append(fractionText);
        else return Sign(text.ToString(), negative, layout);

        // The inch mark of the option's symbol; the double quote when the option has none.
        string markId = InchMark;
        if (layout.Symbol.Length != 0)
        {
            if (!settings.Symbols.TryGetValue(layout.Symbol, out var chosen)) return null;
            if (chosen.Text.Length != 0) markId = layout.Symbol;
        }
        if (!TryAttachSymbol(text.ToString(), markId, settings, out string body)) return null;
        return Sign(body, negative, layout);
    }
}
