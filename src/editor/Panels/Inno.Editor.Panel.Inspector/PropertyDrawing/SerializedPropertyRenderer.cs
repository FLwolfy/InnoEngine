using System;
using System.Collections.Generic;
using Inno.Core.Logging;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Resolves drawers and renders serialized property paths with isolated error handling.
/// </summary>
public sealed class SerializedPropertyRenderer
{
    private readonly Dictionary<string, string> m_failureStates = new(StringComparer.Ordinal);
    private readonly PropertyDrawerRegistry m_drawers;
    private readonly EditorInteractions m_interactions;
    private readonly SceneEdits m_edits;

    internal SerializedPropertyRenderer(
        PropertyDrawerRegistry drawers,
        EditorInteractions interactions,
        SceneEdits edits)
    {
        m_drawers = drawers ?? throw new ArgumentNullException(nameof(drawers));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        m_edits = edits ?? throw new ArgumentNullException(nameof(edits));
    }

    /// <summary>
    /// Draws a root serialized property.
    /// </summary>
    /// <param name="editorContext">Shared editor context.</param>
    /// <param name="owner">The live scene object that owns the root property.</param>
    /// <param name="ownerPath">Stable owner path.</param>
    /// <param name="property">Serialized property.</param>
    public void Draw(
        EditorContext editorContext,
        EngineObject owner,
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
        EngineObject owner,
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
        {
            try
            {
                IPropertyDrawer drawer = m_drawers.Resolve(propertyType);
                EditorWidget.Disabled(context.isReadOnly, () => drawer.Draw(context));
                m_failureStates.Remove(path);
            }
            catch (Exception exception)
            {
                NativeImGui.TextColored(
                    EditorPalette.error,
                    $"Error: {exception.Message}");
                string failureState = $"{exception.GetType().FullName}|{exception.Message}";
                if (!m_failureStates.TryGetValue(path, out string? previous) ||
                    !string.Equals(previous, failureState, StringComparison.Ordinal))
                {
                    Log.Error("Inspector failed to draw property '{0}': {1}", path, exception);
                    m_failureStates[path] = failureState;
                }
            }
        });
    }

    internal IPropertyDrawer Resolve(Type propertyType) => m_drawers.Resolve(propertyType);
}
