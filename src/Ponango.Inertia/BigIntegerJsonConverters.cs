using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ponango.Inertia;

/// <summary>
/// Writes integers outside JavaScript's safe range (±(2^53 − 1)) as <c>{"$bigint": "&lt;digits&gt;"}</c> markers,
/// which Inertia clients (3.8.0+) revive as native <c>BigInt</c> values on pages flagged with
/// <c>preserveBigIntegers</c>. Integers inside the safe range are written as plain numbers.
/// </summary>
internal sealed class BigIntegerConverterFactory : JsonConverterFactory
{
    internal const long MaxSafeInteger = 9007199254740991;
    internal const string MarkerKey = "$bigint";

    public override bool CanConvert(Type typeToConvert)
        => typeToConvert == typeof(long)
           || typeToConvert == typeof(ulong)
           || typeToConvert == typeof(Int128)
           || typeToConvert == typeof(UInt128)
           || typeToConvert == typeof(BigInteger);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (typeToConvert == typeof(long)) return new Converter<long>(v => v > MaxSafeInteger || v < -MaxSafeInteger, (w, v) => w.WriteNumberValue(v));
        if (typeToConvert == typeof(ulong)) return new Converter<ulong>(v => v > MaxSafeInteger, (w, v) => w.WriteNumberValue(v));
        if (typeToConvert == typeof(Int128)) return new Converter<Int128>(v => v > MaxSafeInteger || v < -MaxSafeInteger, WriteRaw);
        if (typeToConvert == typeof(UInt128)) return new Converter<UInt128>(v => v > (UInt128)MaxSafeInteger, WriteRaw);
        return new Converter<BigInteger>(v => BigInteger.Abs(v) > MaxSafeInteger, WriteRaw);
    }

    static void WriteRaw<T>(Utf8JsonWriter writer, T value) where T : IFormattable
        => writer.WriteRawValue(value.ToString(null, CultureInfo.InvariantCulture), skipInputValidation: true);

    internal static void WriteMarker(Utf8JsonWriter writer, string digits)
    {
        writer.WriteStartObject();
        writer.WriteString(MarkerKey, digits);
        writer.WriteEndObject();
    }

    private sealed class Converter<T> : JsonConverter<T> where T : IFormattable
    {
        private readonly Func<T, bool> _isUnsafe;
        private readonly Action<Utf8JsonWriter, T> _writeNumber;

        public Converter(Func<T, bool> isUnsafe, Action<Utf8JsonWriter, T> writeNumber)
        {
            _isUnsafe = isUnsafe;
            _writeNumber = writeNumber;
        }

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException("Big integer markers are only written, never read.");

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (_isUnsafe(value))
                WriteMarker(writer, value.ToString(null, CultureInfo.InvariantCulture));
            else
                _writeNumber(writer, value);
        }

        // Dictionary keys are JSON strings already, so they keep their exact digits.
        public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            => writer.WritePropertyName(value.ToString(null, CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Rewrites unsafe integers inside <see cref="JsonElement"/> values (for example flash data, which is stored as
/// JSON and read back as <see cref="JsonElement"/>) into <c>$bigint</c> markers.
/// </summary>
internal sealed class BigIntegerJsonElementConverter : JsonConverter<JsonElement>
{
    public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => JsonElement.ParseValue(ref reader);

    public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value, options);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    Write(writer, item, options);
                writer.WriteEndArray();
                break;

            case JsonValueKind.Number when IsUnsafeInteger(value, out var digits):
                BigIntegerConverterFactory.WriteMarker(writer, digits);
                break;

            default:
                value.WriteTo(writer);
                break;
        }
    }

    static bool IsUnsafeInteger(JsonElement number, out string digits)
    {
        digits = number.GetRawText();

        // Fractions and exponents are not integers; JavaScript's own number handling applies to them.
        if (digits.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0)
            return false;

        return BigInteger.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)
               && BigInteger.Abs(integer) > BigIntegerConverterFactory.MaxSafeInteger;
    }
}
