using System;
using System.Collections.Generic;

using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Editor.Scene;
using Inno.Engine.Scene;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

/// <summary>Draws the project-defined tag picker used by GameObject target headers.</summary>
internal sealed class GameObjectTagSelector
{
    private readonly SceneProjectSettingsModule m_settings;
    private readonly SceneEdits m_edits;

    /// <summary>Creates a tag selector backed by the effective Project Settings snapshot.</summary>
    /// <param name="settings">The project Scene-classification settings module.</param>
    /// <param name="edits">The Scene editing service used to record GameObject tag changes.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="settings"/> or <paramref name="edits"/> is <see langword="null"/>.
    /// </exception>
    internal GameObjectTagSelector(SceneProjectSettingsModule settings, SceneEdits edits)
    {
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
        m_edits = edits ?? throw new ArgumentNullException(nameof(edits));
    }

    /// <summary>Draws a compact tag selector for one live GameObject.</summary>
    /// <param name="target">The GameObject whose tag should be displayed and edited.</param>
    /// <param name="width">The width reserved for the selector.</param>
    internal void Draw(GameObject target, float width)
    {
        ArgumentNullException.ThrowIfNull(target);
        IReadOnlyList<string> tags = m_settings.tagCatalog.GetTags();
        string preview = m_settings.tagCatalog.IsDefined(target.tag)
            ? target.tag
            : $"{target.tag} (Undefined)";
        EditorWidget.LabelChip("Tag", EditorPalette.inspectorTagLabel);
        NativeImGui.SameLine(0f, 0f);
        NativeImGui.SetNextItemWidth(MathF.Max(1f, width));
        if (!NativeImGui.BeginCombo(
                $"##game_object_tag_{target.identity.persistentId:N}",
                preview,
                ImGuiComboFlags.None))
        {
            return;
        }
        try
        {
            for (int i = 0; i < tags.Count; i++)
            {
                string tag = tags[i];
                if (NativeImGui.Selectable(
                        tag,
                        string.Equals(target.tag, tag, StringComparison.Ordinal)))
                {
                    m_edits.SetGameObjectTag(target, tag);
                }
            }
        }
        finally
        {
            NativeImGui.EndCombo();
        }
    }

    /// <summary>Determines whether the current Project Settings catalog defines a tag.</summary>
    /// <param name="tag">The tag value to resolve.</param>
    /// <returns><see langword="true"/> when the tag is currently defined.</returns>
    internal bool IsTagDefined(string tag)
        => m_settings.tagCatalog.IsDefined(tag);
}
