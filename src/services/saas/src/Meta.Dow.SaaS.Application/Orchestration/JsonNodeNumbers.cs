using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// Reads JSON numbers by their actual CLR / JsonElement type.
/// JsonValue.TryGetValue&lt;T&gt; throws when T does not match the boxed type
/// (Int32 vs Int64 vs Double), so callers must not use GetValue&lt;int&gt; directly.
/// </summary>
internal static class JsonNodeNumbers
{
    public static int? ToInt32(JsonNode? node)
    {
        var n = ToInt64(node);
        if (n is null)
        {
            return null;
        }

        if (n.Value > int.MaxValue)
        {
            return int.MaxValue;
        }

        if (n.Value < int.MinValue)
        {
            return int.MinValue;
        }

        return (int)n.Value;
    }

    public static long? ToInt64(JsonNode? node)
    {
        return ToNumberObject(node) switch
        {
            sbyte v => v,
            byte v => v,
            short v => v,
            ushort v => v,
            int v => v,
            uint v => v,
            long v => v,
            ulong v => v > long.MaxValue ? long.MaxValue : (long)v,
            decimal d => (long)d,
            double dbl => (long)dbl,
            float f => (long)f,
            _ => null
        };
    }

    public static decimal? ToDecimal(JsonNode? node)
    {
        return ToNumberObject(node) switch
        {
            sbyte v => v,
            byte v => v,
            short v => v,
            ushort v => v,
            int v => v,
            uint v => v,
            long v => v,
            ulong v => v,
            decimal d => d,
            double dbl => (decimal)dbl,
            float f => (decimal)f,
            _ => null
        };
    }

    public static object? ToNumberObject(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        return value.GetValueKind() switch
        {
            JsonValueKind.Number => UnwrapNumber(value),
            JsonValueKind.String => ParseNumericString(value.GetValue<string>()),
            _ => null
        };
    }

    private static object? UnwrapNumber(JsonValue value)
    {
        if (TryGet(value, out int i))
        {
            return i;
        }

        if (TryGet(value, out long l))
        {
            return l;
        }

        if (TryGet(value, out JsonElement element) && element.ValueKind == JsonValueKind.Number)
        {
            return FromElement(element);
        }

        if (TryGet(value, out decimal d))
        {
            return d;
        }

        if (TryGet(value, out double dbl))
        {
            return dbl;
        }

        if (TryGet(value, out float f))
        {
            return f;
        }

        if (TryGet(value, out uint ui))
        {
            return ui;
        }

        if (TryGet(value, out ulong ul))
        {
            return ul;
        }

        return FromElement(value.Deserialize<JsonElement>());
    }

    private static object? FromElement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        if (element.TryGetInt64(out var l))
        {
            return l is >= int.MinValue and <= int.MaxValue ? (int)l : l;
        }

        if (element.TryGetDecimal(out var d))
        {
            return d;
        }

        if (element.TryGetDouble(out var dbl))
        {
            return dbl;
        }

        return null;
    }

    private static object? ParseNumericString(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
        {
            return l is >= int.MinValue and <= int.MaxValue ? (int)l : l;
        }

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }

        return null;
    }

    private static bool TryGet<T>(JsonValue value, out T result)
    {
        result = default!;
        try
        {
            return value.TryGetValue(out result!);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
