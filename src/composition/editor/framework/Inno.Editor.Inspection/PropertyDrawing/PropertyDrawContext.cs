using System;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Interactions;

namespace Inno.Editor.Inspection;

/// <summary>
/// Encapsulates one editable property path and its drawing services.
/// </summary>
public sealed class PropertyDrawContext
{
    private readonly Func<object?> m_getter;
    private readonly Action<object?> m_setter;
    private readonly SerializedPropertyRenderer m_renderer;
    private readonly IInspectionPropertyEditService m_edits;
    private readonly object m_owner;
    private readonly string m_rootPropertyName;

    /// <summary>
    /// Gets the shared editor context.
    /// </summary>
    public EditorContext editorContext { get; }

    /// <summary>
    /// Gets the active editor interaction entry point.
    /// </summary>
    public EditorInteractions interactions { get; }

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

    internal object owner => m_owner;

    internal PropertyDrawContext(
        EditorContext editorContext,
        EditorInteractions interactions,
        IInspectionPropertyEditService edits,
        object owner,
        string rootPropertyName,
        string path,
        string label,
        Type propertyType,
        PropertyVisibility visibility,
        Func<object?> getter,
        Action<object?> setter,
        SerializedPropertyRenderer renderer)
    {
        this.editorContext = editorContext;
        this.interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        m_edits = edits ?? throw new ArgumentNullException(nameof(edits));
        m_owner = owner ?? throw new ArgumentNullException(nameof(owner));
        m_rootPropertyName = string.IsNullOrWhiteSpace(rootPropertyName)
            ? throw new ArgumentException("The root property name is required.", nameof(rootPropertyName))
            : rootPropertyName;
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
    /// <returns>
    /// The latest value returned by the serialized property's getter.
    /// </returns>
    public object? GetValue() => m_getter();

    /// <summary>
    /// Tries to read text-edit state scoped to this renderer, inspected owner, and property path.
    /// </summary>
    /// <param name="key">
    /// Drawer-local state identity within the current property path.
    /// </param>
    /// <param name="value">
    /// Receives the stored text when the state exists.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when text state exists for the exact owner and property path.
    /// </returns>
    public bool TryGetTextState(string key, out string? value)
        => m_renderer.TryGetTextState(m_owner, path, key, out value);

    /// <summary>
    /// Stores text-edit state scoped to this renderer, inspected owner, and property path.
    /// </summary>
    /// <param name="key">
    /// Drawer-local state identity within the current property path.
    /// </param>
    /// <param name="value">
    /// Current neutral text state to retain between frames.
    /// </param>
    public void SetTextState(string key, string value)
        => m_renderer.SetTextState(m_owner, path, key, value);

    /// <summary>
    /// Removes text-edit state for this renderer, inspected owner, and property path.
    /// </summary>
    /// <param name="key">
    /// Drawer-local state identity within the current property path.
    /// </param>
    public void ClearTextState(string key)
        => m_renderer.ClearTextState(m_owner, path, key);

    /// <summary>
    /// Assigns a value when the property is writable.
    /// </summary>
    /// <param name="value">
    /// New value.
    /// </param>
    public void SetValue(object? value)
    {
        if (isReadOnly)
            return;
        if (Equals(m_getter(), value))
            return;
        _ = m_edits.ChangeProperty(
            m_owner,
            m_rootPropertyName,
            () => m_setter(value),
            $"Change {label}");
    }

    /// <summary>
    /// Draws a nested value whose setter writes through this property path.
    /// </summary>
    /// <param name="childName">
    /// The stable child member name appended to this property path.
    /// </param>
    /// <param name="childType">
    /// The declared type used to resolve a property drawer.
    /// </param>
    /// <param name="getter">
    /// The callback that reads the latest child value.
    /// </param>
    /// <param name="setter">
    /// The callback that writes an edited child value.
    /// </param>
    /// <param name="readOnly">
    /// Whether the child should be presented without assignment support.
    /// </param>
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
            m_owner,
            m_rootPropertyName,
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
    /// <param name="property">
    /// Nested property.
    /// </param>
    public void DrawChild(SerializedProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        m_renderer.Draw(
            editorContext,
            m_owner,
            m_rootPropertyName,
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
    /// <param name="childName">
    /// The stable child member name appended to this property path.
    /// </param>
    /// <param name="childType">
    /// The declared type used to resolve a property drawer.
    /// </param>
    /// <param name="getter">
    /// The callback that reads the latest child value.
    /// </param>
    /// <param name="setter">
    /// The callback that writes an edited child value.
    /// </param>
    /// <param name="readOnly">
    /// Whether the child should be presented without assignment support.
    /// </param>
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
        m_renderer.DrawInline(
            editorContext,
            m_owner,
            m_rootPropertyName,
            $"{path}.{childName}",
            childName,
            childType,
            childVisibility,
            getter,
            setter);
    }
}
