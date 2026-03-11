using System;

namespace Inno.Core.Serialization;

/// <summary>
/// Represents a discoverable property in the editor and in the serialization pipeline.
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

    #endregion

    #region Construction

    internal SerializedProperty(
        string name,
        Type propertyType,
        Func<object?> getter,
        Action<object?> setter,
        PropertyVisibility visibility)
    {
        this.name = name;
        this.propertyType = propertyType;
        this.visibility = visibility;
        m_getter = getter;
        m_setter = setter;
    }

    #endregion

    #region Public API

    /// <summary>
    /// Gets the current value.
    /// </summary>
    /// <returns>The value produced by the getter delegate.</returns>
    public object? GetValue() => m_getter();

    /// <summary>
    /// Sets the current value.
    /// </summary>
    /// <param name="value">The value to assign.</param>
    public void SetValue(object? value) => m_setter(value);

    #endregion
}
