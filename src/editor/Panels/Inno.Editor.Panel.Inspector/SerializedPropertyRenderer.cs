using System;
using System.Collections.Generic;
using Inno.Core.Logging;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.Widgets;
using Inno.Editor.Interactions;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Resolves drawers and renders serialized property paths with isolated error handling.
/// </summary>
public sealed class SerializedPropertyRenderer
{
    private readonly HashSet<string> m_loggedErrors = new(StringComparer.Ordinal);
    private readonly PropertyDrawerRegistry m_drawers;
    private readonly EditorInteractions m_interactions;

    internal SerializedPropertyRenderer(
        PropertyDrawerRegistry drawers,
        EditorInteractions interactions)
    {
        m_drawers = drawers ?? throw new ArgumentNullException(nameof(drawers));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
    }

    /// <summary>
    /// Draws a root serialized property.
    /// </summary>
    /// <param name="editorContext">Shared editor context.</param>
    /// <param name="ownerPath">Stable owner path.</param>
    /// <param name="property">Serialized property.</param>
    public void Draw(EditorContext editorContext, string ownerPath, SerializedProperty property)
    {
        ArgumentNullException.ThrowIfNull(editorContext);
        ArgumentNullException.ThrowIfNull(property);
        Draw(
            editorContext,
            $"{ownerPath}.{property.name}",
            property.name,
            property.propertyType,
            property.visibility,
            property.GetValue,
            property.SetValue);
    }

    internal void Draw(
        EditorContext editorContext,
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
            path,
            ImGuiWidget.NicifyName(label),
            propertyType,
            visibility,
            getter,
            setter,
            this);

        ImGuiWidget.PropertyRow(path, context.label, () =>
        {
            try
            {
                IPropertyDrawer drawer = m_drawers.Resolve(propertyType);
                ImGuiWidget.Disabled(context.isReadOnly, () => drawer.Draw(context));
            }
            catch (Exception exception)
            {
                NativeImGui.TextColored(
                    EditorPalette.error,
                    $"Error: {exception.Message}");
                string key = $"{path}|{exception.GetType().FullName}|{exception.Message}";
                if (m_loggedErrors.Add(key))
                {
                    Log.Error("Inspector failed to draw property '{0}': {1}", path, exception);
                }
            }
        });
    }

    internal IPropertyDrawer Resolve(Type propertyType) => m_drawers.Resolve(propertyType);
}
