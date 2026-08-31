using System;

namespace Inno.Core.Serialization.Converters;

/// <summary>
/// Defines the single advanced extension contract for serializing a specific value type.
/// </summary>
/// <typeparam name="T">The value contract handled by the converter.</typeparam>
public abstract class SerializationConverter<T>
{
    /// <summary>
    /// Writes a value into the current structured object.
    /// </summary>
    /// <param name="writer">The operation-scoped writer.</param>
    /// <param name="value">The value to write.</param>
    public abstract void Write(SerializationWriter writer, T value);

    /// <summary>
    /// Reads and creates a value from the current structured object.
    /// </summary>
    /// <param name="reader">The operation-scoped reader.</param>
    /// <returns>The restored value.</returns>
    public abstract T Read(SerializationReader reader);

    /// <summary>
    /// Restores data into an existing value.
    /// </summary>
    /// <param name="reader">The operation-scoped reader.</param>
    /// <param name="target">The existing target value.</param>
    /// <exception cref="NotSupportedException">Thrown when the converter does not support restoring existing values.</exception>
    public virtual void Restore(SerializationReader reader, T target)
        => throw new NotSupportedException(
            $"Serialization converter '{GetType().FullName}' does not support restoring existing '{typeof(T).FullName}' values.");
}
