using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Inno.Editor.Core;

/// <summary>
/// Reads one provider's isolated project workspace state without exposing mutable storage.
/// </summary>
public sealed class EditorWorkspaceStateReader
{
    private readonly JsonObject m_values;

    /// <summary>
    /// Creates a workspace state reader for an optional provider payload.
    /// </summary>
    /// <param name="payload">The stored JSON object payload, or <see langword="null"/> when no state exists.</param>
    /// <exception cref="JsonException">Thrown when <paramref name="payload"/> is not a valid JSON object.</exception>
    public EditorWorkspaceStateReader(string? payload)
    {
        hasState = payload is not null;
        m_values = payload is null
            ? []
            : JsonNode.Parse(payload) as JsonObject
              ?? throw new JsonException("Editor workspace state must be a JSON object.");
    }

    /// <summary>
    /// Gets whether compatible storage was found for the provider.
    /// </summary>
    public bool hasState { get; }

    /// <summary>
    /// Tries to deserialize one value by its provider-local key.
    /// </summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The stable key local to the current provider.</param>
    /// <param name="value">The deserialized value when successful.</param>
    /// <returns><see langword="true"/> when the key exists and contains a compatible value.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is empty.</exception>
    public bool TryGet<T>(string key, [MaybeNullWhen(false)] out T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!m_values.TryGetPropertyValue(key, out JsonNode? node) || node is null)
        {
            value = default;
            return false;
        }
        try
        {
            value = node.Deserialize<T>();
            return value is not null || default(T) is null;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
        catch (NotSupportedException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Reads one value or returns a caller-provided fallback when the value is absent or incompatible.
    /// </summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The stable key local to the current provider.</param>
    /// <param name="fallback">The value returned when the stored value cannot be read.</param>
    /// <returns>The stored compatible value or <paramref name="fallback"/>.</returns>
    public T Get<T>(string key, T fallback)
        => TryGet(key, out T? value) ? value! : fallback;
}
