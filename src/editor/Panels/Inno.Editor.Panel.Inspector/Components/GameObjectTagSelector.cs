using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Editor.Inspection;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Draws the project tag picker and manages its transient add-tag input state.
/// </summary>
internal sealed class GameObjectTagSelector
{
    private const nuint C_TAG_BUFFER_SIZE = 128;

    private readonly GameObjectTagCatalog m_catalog;
    private readonly SceneEdits m_edits;
    private string m_newTag = string.Empty;

    /// <summary>
    /// Creates a tag selector backed by one project catalog.
    /// </summary>
    /// <param name="catalog">The project tag catalog to present and mutate.</param>
    /// <param name="edits">The Scene editing service used to record GameObject tag changes.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="catalog"/> or <paramref name="edits"/> is
    /// <see langword="null"/>.
    /// </exception>
    internal GameObjectTagSelector(GameObjectTagCatalog catalog, SceneEdits edits)
    {
        m_catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        m_edits = edits ?? throw new ArgumentNullException(nameof(edits));
    }

    /// <summary>
    /// Draws a compact tag selector for one live game object.
    /// </summary>
    /// <param name="context">The current Inspector drawing context.</param>
    /// <param name="target">The game object whose tag should be displayed.</param>
    internal void Draw(InspectionDrawContext context, GameObject target)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(target);
        SynchronizeLoadedTags();

        NativeImGui.TextUnformatted("Tag");
        NativeImGui.SameLine(0f, EditorWidget.style.inspectorHeaderControlSpacing);
        NativeImGui.SetNextItemWidth(MathF.Max(1f, NativeImGui.GetContentRegionAvail().X));
        if (!NativeImGui.BeginCombo(
                $"##game_object_tag_{target.identity.persistentId:N}",
                target.tag,
                ImGuiComboFlags.None))
        {
            return;
        }

        DrawCreateRow(context, target);
        NativeImGui.Separator();
        IReadOnlyList<string> tags = m_catalog.GetTags();
        for (int i = 0; i < tags.Count; i++)
            DrawTagRow(context, target, tags[i]);
        NativeImGui.EndCombo();
    }

    private void DrawCreateRow(InspectionDrawContext context, GameObject target)
    {
        Vector2 actionSize = EditorWidget.GetCompactIconSize();
        float spacing = NativeImGui.GetStyle().ItemSpacing.X;
        float inputWidth = MathF.Max(
            1f,
            NativeImGui.GetContentRegionAvail().X - actionSize.X - spacing);
        NativeImGui.SetNextItemWidth(inputWidth);
        bool submit = NativeImGui.InputTextWithHint(
            $"##new_game_object_tag_{target.identity.persistentId:N}",
            "Add tag...",
            ref m_newTag,
            C_TAG_BUFFER_SIZE,
            ImGuiInputTextFlags.EnterReturnsTrue);
        NativeImGui.SameLine(0f, spacing);
        submit |= EditorWidget.ClickableIcon(
            $"add_game_object_tag_{target.identity.persistentId:N}",
            ImGuiIcon.Plus,
            "Add and select tag");
        if (!submit || string.IsNullOrWhiteSpace(m_newTag))
            return;

        string tag = m_newTag.Trim();
        _ = m_catalog.Add(tag);
        m_edits.SetGameObjectTag(target, tag);
        m_newTag = string.Empty;
        NativeImGui.CloseCurrentPopup();
    }

    private void DrawTagRow(
        InspectionDrawContext context,
        GameObject target,
        string tag)
    {
        bool isDefault = string.Equals(tag, GameObject.defaultTag, StringComparison.Ordinal);
        Vector2 actionSize = EditorWidget.GetCompactIconSize();
        float spacing = NativeImGui.GetStyle().ItemSpacing.X;
        float selectableWidth = isDefault
            ? NativeImGui.GetContentRegionAvail().X
            : MathF.Max(1f, NativeImGui.GetContentRegionAvail().X - actionSize.X - spacing);
        if (NativeImGui.Selectable(
                $"{tag}##tag_{target.identity.persistentId:N}_{tag}",
                string.Equals(target.tag, tag, StringComparison.Ordinal),
                ImGuiSelectableFlags.None,
                new Vector2(selectableWidth, 0f)))
        {
            m_edits.SetGameObjectTag(target, tag);
        }

        if (isDefault)
            return;

        NativeImGui.SameLine(0f, spacing);
        bool remove = EditorWidget.ClickableIcon(
            $"remove_game_object_tag_{tag}",
            ImGuiIcon.TrashCan,
            "Delete tag and reset matching GameObjects");
        if (remove)
            RemoveTag(context, tag);
    }

    private void SynchronizeLoadedTags()
        => m_catalog.Synchronize(
            SceneManager.loadedScenes.SelectMany(static scene => scene.GetObjects())
                .Select(static gameObject => gameObject.tag));

    private void RemoveTag(InspectionDrawContext context, string tag)
    {
        GameObject[] matches = SceneManager.loadedScenes
            .SelectMany(scene => scene.FindObjectsWithTag(tag))
            .ToArray();
        using EditorHistoryTransaction transaction = context.interactions.history.BeginTransaction(
            $"Delete Tag '{tag}'");
        EditorHistoryResult catalogResult = context.interactions.history.Execute(
            $"Delete Tag Definition '{tag}'",
            () =>
            {
                _ = m_catalog.Remove(tag);
                return EditorHistoryResult.Success();
            },
            () =>
            {
                _ = m_catalog.Add(tag);
                return EditorHistoryResult.Success();
            });
        if (!catalogResult.succeeded)
        {
            _ = transaction.Rollback();
            return;
        }

        for (int i = 0; i < matches.Length; i++)
        {
            m_edits.SetGameObjectTag(
                matches[i],
                GameObject.defaultTag,
                $"Reset Tag on '{matches[i].name}'");
        }
        transaction.Commit();
    }
}
