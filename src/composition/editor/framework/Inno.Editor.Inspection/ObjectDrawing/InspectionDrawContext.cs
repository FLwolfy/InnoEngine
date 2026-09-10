using System;

using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Core.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Inno.Editor.Inspection;

/// <summary>
/// Provides services and state to an inspector drawer.
/// </summary>
public sealed class InspectionDrawContext
{
    private readonly InspectionDrawerRegistry m_drawers;
    private readonly IReadOnlyList<SerializedProperty> m_serializedProperties;
    /// <summary>
    /// Gets the shared editor context.
    /// </summary>
    public EditorContext editorContext { get; }

    /// <summary>
    /// Gets the active editor interaction entry point.
    /// </summary>
    public EditorInteractions interactions { get; }

    /// <summary>
    /// Gets the selected target.
    /// </summary>
    public object target { get; }

    /// <summary>
    /// Gets the serialized property renderer.
    /// </summary>
    public SerializedPropertyRenderer properties { get; }

    /// <summary>
    /// Draws an exact target-specific body inside the caller's existing Inspector card.
    /// </summary>
    /// <param name="target">
    /// Nested target whose exact registered drawer should render.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an exact drawer rendered the target.
    /// </returns>
    public bool TryDrawInline(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!m_drawers.TryResolveExact(
                editorContext,
                target,
                properties,
                out IInspectionDrawer? drawer,
                out InspectionDrawContext? context)
            || drawer is null
            || context is null)
        {
            return false;
        }
        drawer.Draw(context);
        return true;
    }

    /// <summary>
    /// Gets whether the target exposes one runtime-visible serialized property.
    /// </summary>
    /// <param name="propertyName">
    /// Exact serialized property name.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the property is available.
    /// </returns>
    public bool HasProperty(string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        return m_serializedProperties.Any(property =>
            string.Equals(property.name, propertyName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Reads one runtime-visible serialized property from the target.
    /// </summary>
    /// <typeparam name="T">
    /// Expected property value type.
    /// </typeparam>
    /// <param name="propertyName">
    /// Exact serialized property name.
    /// </param>
    /// <returns>
    /// The current strongly typed property value.
    /// </returns>
    public T GetProperty<T>(string propertyName)
    {
        SerializedProperty property = RequireProperty(propertyName);
        object? value = property.GetValue();
        return value is T typed
            ? typed
            : throw new InvalidOperationException(
                $"Property '{propertyName}' does not contain '{typeof(T).FullName}'.");
    }

    /// <summary>
    /// Draws one named property with its ordinary editor, history, and validation behavior.
    /// </summary>
    /// <param name="propertyName">
    /// Exact serialized property name.
    /// </param>
    public void DrawProperty(string propertyName)
    {
        SerializedProperty property = RequireProperty(propertyName);
        properties.Draw(
            editorContext,
            target,
            $"inline.{RuntimeHelpers.GetHashCode(target)}",
            property);
    }

    private SerializedProperty RequireProperty(string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        return m_serializedProperties.FirstOrDefault(property =>
                   string.Equals(property.name, propertyName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Target '{target.GetType().FullName}' has no visible serialized property '{propertyName}'.");
    }

    internal InspectionDrawContext(
        EditorContext editorContext,
        EditorInteractions interactions,
        object target,
        SerializedPropertyRenderer properties,
        InspectionDrawerRegistry drawers,
        IReadOnlyList<SerializedProperty> serializedProperties)
    {
        this.editorContext = editorContext ?? throw new ArgumentNullException(nameof(editorContext));
        this.interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        this.target = target ?? throw new ArgumentNullException(nameof(target));
        this.properties = properties ?? throw new ArgumentNullException(nameof(properties));
        m_drawers = drawers ?? throw new ArgumentNullException(nameof(drawers));
        m_serializedProperties = serializedProperties
            ?? throw new ArgumentNullException(nameof(serializedProperties));
    }
}
