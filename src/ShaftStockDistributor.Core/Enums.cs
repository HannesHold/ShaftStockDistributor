using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShaftStockDistributor.Core;

/// <summary>How often an article arrives. A arrives often, C rarely.</summary>
public enum AbcClass
{
    A = 0,
    B = 1,
    C = 2
}

/// <summary>Which crate dimension points along the shaft (front to back).</summary>
public enum FlowAxis
{
    Length = 0,
    Width = 1,
    Height = 2
}

/// <summary>Who may pull shafts in a segment. Exclusive owners other than shuttle 1 and shuttle 2 use <see cref="Exclusive"/>.</summary>
public enum ZoneKind
{
    Shuttle1Only = 0,
    Shared = 1,
    Shuttle2Only = 2,
    Exclusive = 3
}

/// <summary>
/// Manual keeps the stored assignment. Automatic may move an article onto an empty shaft at runtime.
/// </summary>
public enum AssignmentMode
{
    Manual = 0,
    Automatic = 1
}

/// <summary>
/// Aisle order is the shuttle number: 1 is the leftmost shuttle and cannot pass 2.
/// JSON stays <c>shuttle1</c>, <c>shuttle2</c>, and so on, so older project files still load.
/// </summary>
[JsonConverter(typeof(ShuttleIdJsonConverter))]
public readonly record struct ShuttleId(int Number) : IComparable<ShuttleId>
{
    public static ShuttleId Shuttle1 => new(1);

    public static ShuttleId Shuttle2 => new(2);

    public int CompareTo(ShuttleId other) => Number.CompareTo(other.Number);

    public override string ToString() => Number.ToString(CultureInfo.InvariantCulture);
}

public sealed class ShuttleIdJsonConverter : JsonConverter<ShuttleId>
{
    public override ShuttleId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int number))
        {
            return new ShuttleId(number);
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            string text = reader.GetString() ?? "";
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return new ShuttleId(parsed);
            }

            if (text.StartsWith("shuttle", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(text.AsSpan("shuttle".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int suffix))
            {
                return new ShuttleId(suffix);
            }
        }

        throw new JsonException("A shuttle id must look like \"shuttle1\".");
    }

    public override void Write(Utf8JsonWriter writer, ShuttleId value, JsonSerializerOptions options) =>
        writer.WriteStringValue("shuttle" + value.Number.ToString(CultureInfo.InvariantCulture));
}

public enum ValidationSeverity
{
    Warning = 0,
    Error = 1
}
