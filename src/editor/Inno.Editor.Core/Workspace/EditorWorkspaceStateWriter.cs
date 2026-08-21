using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Inno.Editor.Core;

/// <summary>
/// Writes one provider's isolated, JSON-compatible workspace state.
/// </summary>
public sealed class EditorWorkspaceStateWriter
{
    private readonly JsonObject m_values = [];

    /// <summary>
    /// Creates an empty isolated workspace state writer.
    /// </summary>
    public EditorWorkspaceStateWriter()
    {
    }

    /// <summary>
    /// Stores or replaces one named value using the default workspace JSON representation.
    /// </summary>
    /// <typeparam name="T">The serializable value type.</typeparam>
    /// <param name="key">The stable key local to the current provider.</param>
    /// <param name="value">The value to serialize.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="value"/> cannot be serialized.</exception>
    public void Set<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        m_values[key] = JsonSerializer.SerializeToNode(value);
    }

    /// <summary>
    /// Removes a previously written value from this provider's state.
    /// </summary>
    /// <param name="key">The stable key local to the current provider.</param>
    /// <returns><see langword="true"/> when a value was removed.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is empty.</exception>
    public bool Remove(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return m_values.Remove(key);
    }

    /// <summary>
    /// Exports an independent JSON representation of the values written so far.
    /// </summary>
    /// <returns>A deterministic JSON object payload owned by the caller.</returns>
    public string Export() => m_values.ToJsonString();
}
