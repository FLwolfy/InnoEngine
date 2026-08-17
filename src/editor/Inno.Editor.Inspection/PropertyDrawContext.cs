using System;

using Inno.Core.Serialization;
using Inno.Editor.Core;

namespace Inno.Editor.Inspection;

/// <summary>
/// Encapsulates one editable property path and its drawing services.
/// </summary>
public sealed class PropertyDrawContext
{
    private readonly Func<object?> m_getter;
    private readonly Action<object?> m_setter;
    private readonly SerializedPropertyRenderer m_renderer;

    /// <summary>
    /// Gets the shared editor context.
    /// </summary>
    public EditorContext editorContext { get; }

    /// <summary>
    /// Gets the stable control path.
    /// </summary>
    public string path { get; }

    /// <summary>
    /// Gets the display label.
    /// </summary>
    public string label { get; }

    /// <summary>
    /// Gets the declared property type.
    /// </summary>
    public Type propertyType { get; }

    /// <summary>
    /// Gets the serialization visibility flags.
    /// </summary>
    public PropertyVisibility visibility { get; }

    /// <summary>
    /// Gets whether assignments are disabled.
    /// </summary>
    public bool isReadOnly => (visibility & PropertyVisibility.RuntimeSet) == 0;

    internal PropertyDrawContext(
        EditorContext editorContext,
        string path,
        string label,
        Type propertyType,
        PropertyVisibility visibility,
        Func<object?> getter,
        Action<object?> setter,
        SerializedPropertyRenderer renderer)
    {
        this.editorContext = editorContext;
        this.path = path;
        this.label = label;
        this.propertyType = propertyType;
        this.visibility = visibility;
        m_getter = getter;
        m_setter = setter;
        m_renderer = renderer;
    }

    /// <summary>
    /// Gets the latest value.
    /// </summary>
    public object? GetValue() => m_getter();

    /// <summary>
    /// Assigns a value when the property is writable.
    /// </summary>
    /// <param name="value">New value.</param>
    public void SetValue(object? value)
    {
        if (!isReadOnly)
        {
            m_setter(value);
        }
    }

    /// <summary>
    /// Draws a nested value whose setter writes through this property path.
    /// </summary>
    public void DrawChild(
        string childName,
        Type childType,
        Func<object?> getter,
        Action<object?> setter,
        bool readOnly = false)
    {
        PropertyVisibility childVisibility = readOnly || isReadOnly
            ? PropertyVisibility.Readonly
            : PropertyVisibility.Show;
        m_renderer.Draw(
            editorContext,
            $"{path}.{childName}",
            childName,
            childType,
            childVisibility,
            getter,
            setter);
    }

    /// <summary>
    /// Draws a nested serialized property.
    /// </summary>
    /// <param name="property">Nested property.</param>
    public void DrawChild(SerializedProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        m_renderer.Draw(
            editorContext,
            $"{path}.{property.name}",
            property.name,
            property.propertyType,
            isReadOnly || !property.canWrite ? PropertyVisibility.Readonly : property.visibility,
            property.GetValue,
            property.SetValue);
    }

    /// <summary>
    /// Draws a nested value inline without adding another label row.
    /// </summary>
    public void DrawInlineChild(
        string childName,
        Type childType,
        Func<object?> getter,
        Action<object?> setter,
        bool readOnly = false)
    {
        PropertyVisibility childVisibility = readOnly || isReadOnly
            ? PropertyVisibility.Readonly
            : PropertyVisibility.Show;
        var childContext = new PropertyDrawContext(
            editorContext,
            $"{path}.{childName}",
            childName,
            childType,
            childVisibility,
            getter,
            setter,
            m_renderer);
        IPropertyDrawer drawer = PropertyDrawerRegistry.Resolve(childType);
        drawer.Draw(childContext);
    }
}
