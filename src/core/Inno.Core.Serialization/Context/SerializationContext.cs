using System;
using System.Collections.Generic;

namespace Inno.Core.Serialization;

/// <summary>
/// Provides immutable, operation-independent context to serialization converters.
/// </summary>
public sealed class SerializationContext
{
    private readonly IReadOnlyDictionary<Type, object> m_values;

    private SerializationContext(IReadOnlyDictionary<Type, object> values)
    {
        m_values = values;
    }

    /// <summary>
    /// Gets an empty serialization context.
    /// </summary>
    public static SerializationContext empty { get; } = new(new Dictionary<Type, object>());

    /// <summary>
    /// Returns a new context containing the supplied value under its declared context type.
    /// </summary>
    /// <typeparam name="TContext">The exact context contract type.</typeparam>
    /// <param name="value">The context value to register.</param>
    /// <returns>A new context containing the registered value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
    public SerializationContext With<TContext>(TContext value) where TContext : class
    {
        ArgumentNullException.ThrowIfNull(value);
        var values = new Dictionary<Type, object>(m_values)
        {
            [typeof(TContext)] = value
        };
        return new SerializationContext(values);
    }

    /// <summary>
    /// Attempts to resolve a value registered under the exact context contract type.
    /// </summary>
    /// <typeparam name="TContext">The exact context contract type.</typeparam>
    /// <param name="value">The registered value when available.</param>
    /// <returns><see langword="true"/> when a value is registered; otherwise, <see langword="false"/>.</returns>
    public bool TryGet<TContext>(out TContext? value) where TContext : class
    {
        if (m_values.TryGetValue(typeof(TContext), out object? candidate) && candidate is TContext typed)
        {
            value = typed;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Resolves a value registered under the exact context contract type.
    /// </summary>
    /// <typeparam name="TContext">The exact context contract type.</typeparam>
    /// <returns>The registered context value.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the requested context was not registered.</exception>
    public TContext GetRequired<TContext>() where TContext : class
        => TryGet<TContext>(out TContext? value) && value is not null
            ? value
            : throw new InvalidOperationException(
                $"Serialization context '{typeof(TContext).FullName}' is not registered.");
}
