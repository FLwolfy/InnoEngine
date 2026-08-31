using System;

namespace Inno.Core.Serialization;

/// <summary>
/// Represents a discoverable property in editor and persistence workflows.
/// </summary>
public sealed class SerializedProperty
{
    #region Backing Delegates

    private readonly Func<object?> m_getter;
    private readonly Action<object?> m_setter;

    #endregion

    #region Public State

    /// <summary>
    /// Gets the display and serialization key name.
    /// </summary>
    public string name { get; }

    /// <summary>
    /// Gets the declared CLR type of this property.
    /// </summary>
    public Type propertyType { get; }

    /// <summary>
    /// Gets the visibility of this property.
    /// </summary>
    public PropertyVisibility visibility { get; }

    /// <summary>
    /// Gets whether runtime callers may read this property.
    /// </summary>
    public bool canRead { get; }

    /// <summary>
    /// Gets whether runtime callers may write this property.
    /// </summary>
    public bool canWrite { get; }

    #endregion

    #region Construction

    internal SerializedProperty(
        string name,
        Type propertyType,
        Func<object?> getter,
        Action<object?> setter,
        PropertyVisibility visibility,
        bool canRead,
        bool canWrite)
    {
        this.name = name;
        this.propertyType = propertyType;
        this.visibility = visibility;
        this.canRead = canRead;
        this.canWrite = canWrite;
        m_getter = getter;
        m_setter = setter;
    }

    #endregion

    #region Public API

    /// <summary>
    /// Gets the current value.
    /// </summary>
    /// <returns>The value produced by the getter delegate.</returns>
    /// <exception cref="InvalidOperationException">Thrown when runtime reads are not permitted.</exception>
    public object? GetValue()
    {
        if (!canRead)
            throw new InvalidOperationException($"Serialized property '{name}' does not permit runtime reads.");
        return m_getter();
    }

    /// <summary>
    /// Sets the current value.
    /// </summary>
    /// <param name="value">The value to assign.</param>
    /// <exception cref="InvalidOperationException">Thrown when runtime writes are not permitted.</exception>
    public void SetValue(object? value)
    {
        if (!canWrite)
            throw new InvalidOperationException($"Serialized property '{name}' does not permit runtime writes.");
        m_setter(value);
    }

    #endregion
}
