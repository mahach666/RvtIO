#if !NET9_0_OR_GREATER
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RvtIO.Cli;

/// <summary>Makes the full JSON of the .NET 8 build equal to the one of the .NET 9 and later builds. The JSON writer of
/// .NET 8 writes 17 digits for a double whose shortest round-trip form has 16, and the formatter of .NET 8 differs from the
/// later ones for the powers of two. A number of more than 15 digits and every power of two is written from a parsed
/// <see cref="JsonElement"/>, which keeps the text and the indentation of the writer; every other number is left to the writer.
/// Non-finite values are written as the named literals, as with <see cref="JsonNumberHandling.AllowNamedFloatingPointLiterals"/>.</summary>
internal sealed partial class ShortestDoubleConverter : JsonConverter<double>
{
    private static readonly string[] PowerTexts = PowersOfTwo.Split(';', StringSplitOptions.RemoveEmptyEntries);

    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String ? double.Parse(reader.GetString()!, CultureInfo.InvariantCulture) : reader.GetDouble();

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        if (double.IsNaN(value)) writer.WriteStringValue("NaN");
        else if (double.IsPositiveInfinity(value)) writer.WriteStringValue("Infinity");
        else if (double.IsNegativeInfinity(value)) writer.WriteStringValue("-Infinity");
        else
        {
            string text = value.ToString(CultureInfo.InvariantCulture);
            long bits = BitConverter.DoubleToInt64Bits(value);
            int exponentField = (int)((bits >> 52) & 0x7FF);
            if ((bits & 0xFFFFFFFFFFFFFL) == 0 && exponentField > 0)
                text = (value < 0 ? "-" : "") + PowerTexts[exponentField - 1];
            else if (SignificantDigits(text) <= 15)
            {
                writer.WriteNumberValue(value);
                return;
            }
            using var number = JsonDocument.Parse(text);
            number.RootElement.WriteTo(writer);
        }
    }

    private static int SignificantDigits(string text)
    {
        int count = 0;
        foreach (char c in text)
        {
            if (c == 'E') break;
            if (c is >= '1' and <= '9' || c == '0' && count > 0) count++;
        }
        return count;
    }
}
#endif
