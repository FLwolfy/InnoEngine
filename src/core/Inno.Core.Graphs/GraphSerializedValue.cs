using System;
using System.Text.Json;

namespace Inno.Core.Graphs;

/// <summary>
/// Stores an editor- and runtime-neutral JSON value for graph properties.
/// </summary>
public sealed class GraphSerializedValue
{
    private readonly string m_json;

    /// <summary>
    /// Creates a serialized graph value from one complete JSON value.
    /// </summary>
    /// <param name="json">JSON text containing exactly one value.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="json"/> is empty.</exception>
    /// <exception cref="JsonException">Thrown when <paramref name="json"/> is invalid JSON.</exception>
    public GraphSerializedValue(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using JsonDocument document = JsonDocument.Parse(json);
        m_json = document.RootElement.GetRawText();
    }

    /// <summary>
    /// Gets the normalized JSON representation.
    /// </summary>
    public string json => m_json;

    /// <summary>
    /// Serializes a neutral graph property value.
    /// </summary>
    /// <typeparam name="T">Serializable value type.</typeparam>
    /// <param name="value">Value to serialize.</param>
    /// <param name="options">Optional serializer settings.</param>
    /// <returns>A validated serialized graph value.</returns>
    public static GraphSerializedValue From<T>(T value, JsonSerializerOptions? options = null)
        => new(JsonSerializer.Serialize(value, options));

    /// <summary>
    /// Deserializes the value to the requested neutral data type.
    /// </summary>
    /// <typeparam name="T">Requested result type.</typeparam>
    /// <param name="options">Optional serializer settings.</param>
    /// <returns>The deserialized value, or <see langword="null"/> when JSON represents null.</returns>
    /// <exception cref="JsonException">Thrown when the value cannot be converted to <typeparamref name="T"/>.</exception>
    public T? Deserialize<T>(JsonSerializerOptions? options = null)
        => JsonSerializer.Deserialize<T>(m_json, options);

    /// <inheritdoc />
    public override string ToString() => m_json;
}
