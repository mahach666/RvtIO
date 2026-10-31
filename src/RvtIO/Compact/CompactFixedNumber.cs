using System.Globalization;
using System.Numerics;
using System.Text;

namespace RvtIO.Compact;

/// <summary>The fixed-notation number text written for a double: the digit string of a "round to ndigits places after the point,
/// but a shorter string that reads back as the same double wins" conversion, laid out with a fixed count of decimals. A negative
/// zero prints its sign; NaN and the infinities print as words.</summary>
internal static class CompactFixedNumber
{
    internal static string Format(double value, int decimals)
    {
        if (decimals < 0) decimals = 0;
        bool negative = BitConverter.DoubleToInt64Bits(value) < 0;
        // A degenerate accuracy (zero, tiny) makes the rounded value NaN or infinite: the words are printed.
        if (double.IsNaN(value)) return negative ? "-NaN" : "NaN";
        if (double.IsInfinity(value)) return negative ? "-Infinity" : "Infinity";
        Digits(Math.Abs(value), decimals, out string digits, out int point);
        var text = new StringBuilder();
        if (negative) text.Append('-');
        int remaining = decimals;
        if (point <= 0)
        {
            text.Append('0');
            if (remaining > 0)
            {
                text.Append('.');
                int zeros = Math.Min(-point, remaining);
                text.Append('0', zeros);
                remaining -= zeros;
            }
        }
        else
        {
            int taken = Math.Min(digits.Length, point);
            text.Append(digits, 0, taken);
            text.Append('0', point - taken);
            if (remaining > 0) text.Append('.');
            digits = digits[taken..];
        }
        int copy = Math.Min(digits.Length, remaining);
        text.Append(digits, 0, copy);
        text.Append('0', remaining - copy);
        return text.ToString();
    }

    /// <summary>Digits (no trailing zeros) and the position of the point: value = 0.DIGITS * 10^point.</summary>
    private static void Digits(double magnitude, int decimals, out string digits, out int point)
    {
        if (magnitude == 0) { digits = "0"; point = 1; return; }
        Shortest(magnitude, out digits, out point);
        if (digits.Length - point <= decimals) return;
        // The shortest string needs more places than asked for: round the exact value
        // to `decimals` places, ties to even.
        long bits = BitConverter.DoubleToInt64Bits(magnitude);
        int exponent = (int)((bits >> 52) & 0x7FF);
        long fraction = bits & ((1L << 52) - 1);
        BigInteger mantissa = exponent == 0 ? fraction : fraction | (1L << 52);
        int shift = (exponent == 0 ? 1 : exponent) - 1075;
        BigInteger scaled = mantissa * BigInteger.Pow(10, decimals), rounded;
        if (shift >= 0) rounded = scaled << shift;
        else
        {
            BigInteger divisor = BigInteger.One << -shift;
            rounded = BigInteger.DivRem(scaled, divisor, out BigInteger rest);
            int half = (rest * 2).CompareTo(divisor);
            if (half > 0 || half == 0 && !rounded.IsEven) rounded += 1;
        }
        if (rounded.IsZero) { digits = ""; point = -decimals; return; }
        string all = rounded.ToString(CultureInfo.InvariantCulture);
        point = all.Length - decimals;
        digits = all.TrimEnd('0');
    }

    private static void Shortest(double magnitude, out string digits, out int point)
    {
        string text = magnitude.ToString("R", CultureInfo.InvariantCulture);
        int exponent = 0;
        int e = text.IndexOf('E');
        if (e >= 0) { exponent = int.Parse(text[(e + 1)..], CultureInfo.InvariantCulture); text = text[..e]; }
        int dot = text.IndexOf('.');
        string whole = dot < 0 ? text : text[..dot];
        string tail = dot < 0 ? "" : text[(dot + 1)..];
        string all = whole + tail;
        point = whole.Length + exponent;
        int lead = 0;
        while (lead < all.Length - 1 && all[lead] == '0') lead++;
        all = all[lead..];
        point -= lead;
        digits = all.TrimEnd('0');
        if (digits.Length == 0) digits = "0";
    }
}
