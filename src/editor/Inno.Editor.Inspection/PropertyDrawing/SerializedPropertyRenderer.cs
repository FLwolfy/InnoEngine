using System;
using System.Collections.Generic;
using Inno.Core.Logging;
using Inno.Core.Scripting;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Editor.Interactions;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Inspection;

/// <summary>
/// Resolves drawers and renders serialized property paths with isolated error handling.
/// </summary>
public sealed class SerializedPropertyRenderer
{
    private readonly Dictionary<string, string> m_failureStates = new(StringComparer.Ordinal);
    private readonly PropertyDrawerRegistry m_drawers;
    private readonly EditorInteractions m_interactions;
    private readonly IInspectionPropertyEditService m_edits;

    /// <summary>
    /// Creates a serialized property renderer over one drawer registry and feature-owned edit service.
    /// </summary>
    /// <param name="drawers">The property drawer registry used for runtime type resolution.</param>
    /// <param name="interactions">The active editor interaction entry point.</param>
    /// <param name="edits">The feature-owned service used to apply and record property changes.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="drawers"/>, <paramref name="interactions"/>, or
    /// <paramref name="edits"/> is <see langword="null"/>.
    /// </exception>
    [ScriptingApiIgnore]
    public SerializedPropertyRenderer(
        PropertyDrawerRegistry drawers,
        EditorInteractions interactions,
        IInspectionPropertyEditService edits)
    {
        m_drawers = drawers ?? throw new ArgumentNullException(nameof(drawers));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        m_edits = edits ?? throw new ArgumentNullException(nameof(edits));
    }

    /// <summary>
    /// Draws a root serialized property.
    /// </summary>
    /// <param name="editorContext">Shared editor context.</param>
    /// <param name="owner">The live domain object that owns the root property.</param>
    /// <param name="ownerPath">Stable owner path.</param>
    /// <param name="property">Serialized property.</param>
    public void Draw(
        EditorContext editorContext,
        object owner,
        string ownerPath,
        SerializedProperty property)
    {
        ArgumentNullException.ThrowIfNull(editorContext);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(property);
        Draw(
            editorContext,
            owner,
            property.name,
            $"{ownerPath}.{property.name}",
            property.name,
            property.propertyType,
            property.visibility,
            property.GetValue,
            property.SetValue);
    }

    internal void Draw(
        EditorContext editorContext,
        object owner,
        string rootPropertyName,
        string path,
        string label,
        Type propertyType,
        PropertyVisibility visibility,
        Func<object?> getter,
        Action<object?> setter)
    {
        var context = new PropertyDrawContext(
            editorContext,
            m_interactions,
            m_edits,
            owner,
            rootPropertyName,
            path,
            EditorWidget.NicifyName(label),
            propertyType,
            visibility,
            getter,
            setter,
            this);

        EditorWidget.PropertyRow(path, context.label, () =>
            DrawContent(context));
    }

    internal void DrawInline(
        EditorContext editorContext,
        object owner,
        string rootPropertyName,
        string path,
        string label,
        Type propertyType,
        PropertyVisibility visibility,
        Func<object?> getter,
        Action<object?> setter)
    {
        var context = new PropertyDrawContext(
            editorContext,
            m_interactions,
            m_edits,
            owner,
            rootPropertyName,
            path,
            EditorWidget.NicifyName(label),
            propertyType,
            visibility,
            getter,
            setter,
            this);
        DrawContent(context);
    }

    private void DrawContent(PropertyDrawContext context)
    {
        try
        {
            IPropertyDrawer drawer = m_drawers.Resolve(context.propertyType);
            EditorWidget.Disabled(context.isReadOnly, () => drawer.Draw(context));
            m_failureStates.Remove(context.path);
        }
        catch (Exception exception)
        {
            EditorWidget.ColoredText(EditorPalette.error, $"Error: {exception.Message}");
            string failureState = $"{exception.GetType().FullName}|{exception.Message}";
            if (!m_failureStates.TryGetValue(context.path, out string? previous) ||
                !string.Equals(previous, failureState, StringComparison.Ordinal))
            {
                Log.Error("Inspector failed to draw property '{0}': {1}", context.path, exception);
                m_failureStates[context.path] = failureState;
            }
        }
    }
}
