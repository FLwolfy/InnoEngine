using System;
using System.Collections.Generic;
using System.Text.Json;

using Inno.Editor.Core;

namespace Inno.Editor.Interactions;

internal sealed class EditorJsonState : EditorState
{
    private readonly IReadOnlyDictionary<string, string> m_values;
    private readonly Dictionary<string, string>? m_writableValues;

    internal EditorJsonState()
    {
        m_writableValues = new Dictionary<string, string>(StringComparer.Ordinal);
        m_values = m_writableValues;
    }

    internal EditorJsonState(IReadOnlyDictionary<string, string>? values)
    {
        m_values = values ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public override T Get<T>(string key, T fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!m_values.TryGetValue(key, out string? payload))
            return fallback;
        try
        {
            T? value = JsonSerializer.Deserialize<T>(payload);
            return value is not null || default(T) is null ? value! : fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
        catch (NotSupportedException)
        {
            return fallback;
        }
    }

    /// <inheritdoc />
    public override void Set<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (m_writableValues is null)
        {
            throw new InvalidOperationException(
                "Editor state supplied for restoration cannot be modified.");
        }
        try
        {
            m_writableValues[key] = JsonSerializer.Serialize(value);
        }
        catch (JsonException exception)
        {
            throw new NotSupportedException(
                $"Editor state value '{key}' cannot be serialized.",
                exception);
        }
    }

    internal IReadOnlyDictionary<string, string> Export()
        => m_writableValues ?? throw new InvalidOperationException(
            "Only editor state captured by an extension can be exported.");
}
