using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Core.Identity;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Editor.Settings;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Hierarchy;

/// <summary>
/// Edits the active scene object hierarchy.
/// </summary>
[EditorPanel("scene.hierarchy", "Hierarchy", order: 100)]
internal sealed class HierarchyPanel : EditorPanel
{
    private const nuint C_NAME_BUFFER_SIZE = 512;

    private readonly IEditorSceneWorkspace m_workspace;
    private readonly EditorInteractions m_interactions;
    private readonly SceneEdits m_edits;
    private readonly HierarchySelection m_selection;
    private readonly EditorSettings m_settings;
    private readonly HierarchyDropVisual m_dropVisual = new();
    private readonly HashSet<Guid> m_forceOpenIds = [];
    private readonly HashSet<Guid> m_forceOpenSceneIds = [];
    private readonly HashSet<Guid> m_drawnIds = [];
    private readonly HashSet<Guid> m_initializedSceneIds = [];
    private ulong m_lastRevealedSelectionVersion = ulong.MaxValue;
    private int m_visibleRowIndex;

    /// <inheritdoc />
    public override bool useWindowPadding => false;

    /// <summary>
    /// Creates the hierarchy panel.
    /// </summary>
    internal HierarchyPanel(
        IEditorSceneWorkspace workspace,
        EditorInteractions interactions,
        SceneEdits edits,
        EditorSettings settings)
    {
        m_workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        m_edits = edits ?? throw new ArgumentNullException(nameof(edits));
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
        m_selection = new HierarchySelection(workspace, interactions, m_settings);
    }

    /// <inheritdoc />
    protected override void OnDraw(EditorContext context)
    {
        bool visible = NativeImGui.BeginChild(
            "##HierarchyContent",
            Vector2.Zero,
            ImGuiChildFlags.None,
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.HorizontalScrollbar);
        try
        {
            if (visible)
                DrawContent(context);
        }
        finally
        {
            NativeImGui.EndChild();
        }
    }

    private void DrawContent(EditorContext context)
    {
        m_drawnIds.Clear();
        m_visibleRowIndex = 0;
        m_selection.Prune(context);
        RevealSelection();

        NativeImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, EditorWidget.style.hierarchyItemSpacing);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, EditorWidget.style.compactFramePadding);
        try
        {
            IReadOnlyList<GameScene> scenes = m_workspace.scenes;
            if (scenes.Count > 0)
            {
                Vector2 contentOrigin = NativeImGui.GetCursorScreenPos();
                NativeImGui.SetCursorScreenPos(new Vector2(
                    contentOrigin.X,
                    contentOrigin.Y - ImGuiP.GetCurrentWindow().WindowPadding.Y));
            }
            for (int i = 0; i < scenes.Count; i++)
            {
                DrawScene(context, scenes[i]);
            }

            DrawBlankArea(context);
        }
        finally
        {
            NativeImGui.PopStyleVar(2);
        }
    }

    private void DrawScene(EditorContext context, GameScene scene)
    {
        Guid sceneId = ((IIdentityObject)scene).GetIdentity().persistentId;
        string id = sceneId.ToString("N");
        bool shouldOpen = m_initializedSceneIds.Add(sceneId);
        shouldOpen |= m_forceOpenSceneIds.Remove(sceneId);
        if (shouldOpen)
        {
            EditorWidget.SetNextTreeNodeOpen(true);
        }

        bool selected = m_interactions.selection.TryGet(out GameScene? selectedScene) &&
            ReferenceEquals(selectedScene, scene);
        TreeNodeResult result = EditorWidget.TreeNode(
            $"scene_{id}",
            drawContext => DrawSceneRowContent(context, scene, id, drawContext.rowHeight),
            new TreeNodeOptions
            {
                isLeaf = false,
                selected = selected,
                showBackground = true,
                backgroundColor = EditorPalette.hierarchySceneRow,
                suppressHoverHighlight = false
            });
        try
        {

        if (result.isClicked || result.isDoubleClicked)
            _ = m_interactions.For(HierarchyInteractionIds.C_AREA, scene).Select();

        if (result.isDoubleClicked)
        {
            _ = m_interactions.For(HierarchyInteractionIds.C_AREA, scene)
                .Execute(HierarchyInteractionIds.C_RENAME);
        }

        _ = EditorDragDropRenderer.Source(
            m_interactions.For(HierarchyInteractionIds.C_AREA, scene),
            new EditorDragData(scene, scene.name, () => scene.isLoaded),
            () => NativeImGui.TextUnformatted(scene.name));

        EditorDropPlacement scenePlacement = m_dropVisual.GetScenePlacement(result, NativeImGui.GetMousePos().Y);
        EditorDropWidgetResult sceneDrop = EditorDragDropRenderer.Target(
            m_interactions.For(HierarchyInteractionIds.C_AREA, new HierarchySceneDropTarget(scene)),
            scenePlacement);
        if (sceneDrop.isPreviewing && sceneDrop.status.canDrop)
            m_dropVisual.Draw(result, sceneDrop.status.visual);
        ApplyDropResult(sceneDrop.result);

        DrawSceneContextMenu(context, scene, id);
        if (!result.isOpen)
        {
            return;
        }

        IReadOnlyList<GameObject> roots = m_selection.GetRootObjects(scene);
        for (int i = 0; i < roots.Count; i++)
        {
            DrawObject(context, scene, roots[i]);
        }

        }
        finally
        {
            if (result.isOpen)
                NativeImGui.TreePop();
        }
    }

    private void DrawObject(EditorContext context, GameScene scene, GameObject gameObject)
    {
        if (!gameObject.isRuntimeValid || !m_drawnIds.Add(gameObject.identity.persistentId))
        {
            return;
        }

        Transform transform = gameObject.GetComponent<Transform>();
        bool selected = m_interactions.selection.TryGet(out GameObject? selectedObject)
            && ReferenceEquals(selectedObject, gameObject);
        string id = gameObject.identity.persistentId.ToString("N");
        if (m_forceOpenIds.Remove(gameObject.identity.persistentId))
        {
            EditorWidget.SetNextTreeNodeOpen(true);
        }

        TreeNodeResult result = EditorWidget.TreeNode(
            $"hierarchy_{id}",
            drawContext => DrawRowContent(context, gameObject, drawContext.rowHeight),
            new TreeNodeOptions
            {
                selected = selected,
                isLeaf = transform.children.Count == 0,
                showBackground = true,
                backgroundColor = m_visibleRowIndex % 2 == 0
                    ? EditorPalette.collectionRow
                    : EditorPalette.collectionRowAlternate,
                drawViewportOverlay = () => DrawVisibilityButton(gameObject, id)
            });
        m_visibleRowIndex++;
        try
        {

        if (result.isClicked || result.isDoubleClicked)
        {
            _ = m_interactions.For(HierarchyInteractionIds.C_AREA, gameObject).Select();
        }

        if (result.isDoubleClicked)
        {
            _ = m_interactions.For(HierarchyInteractionIds.C_AREA, gameObject)
                .Execute(HierarchyInteractionIds.C_RENAME);
        }

        _ = EditorDragDropRenderer.Source(
            m_interactions.For(HierarchyInteractionIds.C_AREA, gameObject),
            new EditorDragData(gameObject, gameObject.name, () => gameObject.isRuntimeValid),
            () => NativeImGui.TextUnformatted(gameObject.name));
        EditorDropPlacement placement = m_dropVisual.GetObjectPlacement(result, NativeImGui.GetMousePos().Y);
        EditorDropWidgetResult objectDrop = EditorDragDropRenderer.Target(
            m_interactions.For(HierarchyInteractionIds.C_AREA, new HierarchyObjectDropTarget(gameObject)),
            placement);
        if (objectDrop.isPreviewing && objectDrop.status.canDrop)
            m_dropVisual.Draw(result, objectDrop.status.visual);
        ApplyDropResult(objectDrop.result);

        DrawObjectContextMenu(context, gameObject, id);

        if (!result.isOpen)
        {
            return;
        }

        IReadOnlyList<Transform> children = transform.children;
        for (int i = 0; i < children.Count; i++)
        {
            GameObject? child = children[i].gameObject;
            if (child is not null)
            {
                DrawObject(context, scene, child);
            }
        }

        }
        finally
        {
            if (result.isOpen)
                NativeImGui.TreePop();
        }
    }

    private void DrawRowContent(
        EditorContext context,
        GameObject gameObject,
        float rowHeight)
    {
        string id = gameObject.identity.persistentId.ToString("N");
        bool dimmed = !gameObject.activeInHierarchy;
        if (dimmed)
        {
            NativeImGui.PushStyleColor(ImGuiCol.Text, EditorPalette.hierarchyInactiveText);
        }
        try
        {
            EditorInteraction interaction = m_interactions.For(HierarchyInteractionIds.C_AREA, gameObject);
            bool isRenaming = interaction.IsActive(HierarchyInteractionIds.C_RENAME);
            if (isRenaming)
            {
                EditorWidget.IconText(
                    m_settings
                        .Get("Editor/Appearance/Icons/GameObject")
                        .GetAsString("value", ImGuiIcon.Cube)!,
                    string.Empty,
                    false);
                NativeImGui.SameLine(0f, 0f);
                float visibilityWidth = GetVisibilityButtonWidth();
                float renameWidth = MathF.Max(
                    EditorWidget.style.hierarchyRenameMinimumWidth,
                    NativeImGui.GetContentRegionAvail().X -
                    visibilityWidth -
                    EditorWidget.style.hierarchyRenameTrailingGap);
                _ = interaction.Present(
                    HierarchyInteractionIds.C_RENAME,
                    new InlineRenamePresentation(
                        $"hierarchy_{id}",
                        renameWidth,
                        rowHeight,
                        C_NAME_BUFFER_SIZE));
            }
            else
            {
                EditorWidget.IconText(
                    m_settings
                        .Get("Editor/Appearance/Icons/GameObject")
                        .GetAsString("value", ImGuiIcon.Cube)!,
                    gameObject.name,
                    false);
            }
        }
        finally
        {
            if (dimmed)
                NativeImGui.PopStyleColor();
        }

    }

    private void DrawSceneRowContent(
        EditorContext context,
        GameScene scene,
        string id,
        float rowHeight)
    {
        EditorInteraction interaction = m_interactions.For(HierarchyInteractionIds.C_AREA, scene);
        if (!interaction.IsActive(HierarchyInteractionIds.C_RENAME))
        {
            m_selection.DrawSceneRowContent(context, scene);
            return;
        }

        EditorWidget.IconText(
            m_settings
                .Get("Editor/Appearance/Icons/Scene")
                .GetAsString("value", ImGuiIcon.Cubes)!,
            string.Empty,
            ReferenceEquals(scene, SceneManager.activeScene));
        NativeImGui.SameLine(0f, 0f);
        float renameWidth = MathF.Max(
            EditorWidget.style.hierarchyRenameMinimumWidth,
            NativeImGui.GetContentRegionAvail().X -
            EditorWidget.style.hierarchyRenameTrailingGap);
        _ = interaction.Present(
            HierarchyInteractionIds.C_RENAME,
            new InlineRenamePresentation(
                $"scene_{id}",
                renameWidth,
                rowHeight,
                C_NAME_BUFFER_SIZE));
    }

    private void DrawVisibilityButton(GameObject gameObject, string id)
    {
        string icon = gameObject.activeSelf ? ImGuiIcon.Eye : ImGuiIcon.EyeSlash;
        float buttonWidth = GetVisibilityButtonWidth();
        float right = ImGuiP.GetCurrentWindow().WorkRect.Max.X -
                      EditorWidget.style.windowPadding.X -
                      buttonWidth;
        NativeImGui.SameLine();
        Vector2 cursor = NativeImGui.GetCursorScreenPos();
        NativeImGui.SetCursorScreenPos(new Vector2(MathF.Max(cursor.X, right), cursor.Y));
        if (EditorWidget.ClickableIcon($"hierarchy_visibility_{id}", icon,
                gameObject.activeSelf ? "Deactivate" : "Activate"))
        {
            m_edits.SetGameObjectActive(gameObject, !gameObject.activeSelf);
        }
    }

    private static float GetVisibilityButtonWidth() => EditorWidget.GetCompactIconSize().X;

    private void DrawSceneContextMenu(EditorContext context, GameScene scene, string id)
    {
        if (NativeImGui.IsItemClicked(ImGuiMouseButton.Right))
            _ = m_interactions.For(HierarchyInteractionIds.C_AREA, scene).Select();
        _ = EditorMenuRenderer.ContextMenu(
            $"##scene_context_{id}",
            m_interactions.For(HierarchyInteractionIds.C_AREA, scene));
    }

    private void DrawObjectContextMenu(EditorContext context, GameObject gameObject, string id)
    {
        if (NativeImGui.IsItemClicked(ImGuiMouseButton.Right))
            _ = m_interactions.For(HierarchyInteractionIds.C_AREA, gameObject).Select();
        _ = EditorMenuRenderer.ContextMenu(
            $"##hierarchy_context_{id}",
            m_interactions.For(HierarchyInteractionIds.C_AREA, gameObject));
    }

    private void DrawBlankArea(EditorContext context)
    {
        Vector2 available = NativeImGui.GetContentRegionAvail();
        Vector2 size = new(
            MathF.Max(1f, available.X),
            MathF.Max(EditorWidget.style.hierarchyBlankMinimumHeight, available.Y));
        _ = NativeImGui.InvisibleButton("##hierarchy_blank", size);
        if (NativeImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            _ = m_interactions.For(HierarchyInteractionIds.C_AREA).Select();
        }

        GameScene? activeScene = m_workspace.activeScene;
        if (activeScene is not null)
        {
            EditorDropWidgetResult drop = EditorDragDropRenderer.Target(
                m_interactions.For(
                    HierarchyInteractionIds.C_AREA,
                    new HierarchySceneDropTarget(activeScene)),
                EditorDropPlacement.Into);
            ApplyDropResult(drop.result);
        }

        _ = EditorMenuRenderer.ContextMenu(
            "##hierarchy_blank_context",
            activeScene is null
                ? m_interactions.For(HierarchyInteractionIds.C_AREA)
                : m_interactions.For(HierarchyInteractionIds.C_AREA, activeScene));
    }

    private void RevealSelection()
    {
        ulong selectionVersion = m_interactions.selection.version;
        if (m_lastRevealedSelectionVersion == selectionVersion)
            return;
        m_lastRevealedSelectionVersion = selectionVersion;

        if (!m_interactions.selection.TryGet(out GameObject? gameObject) || !gameObject.isRuntimeValid)
            return;
        m_forceOpenSceneIds.Add(gameObject.scene.identity.persistentId);
        for (Transform? current = gameObject.transform.parent; current is not null; current = current.parent)
        {
            if (current.gameObject is not null)
                m_forceOpenIds.Add(current.gameObject.identity.persistentId);
        }
    }

    private void ApplyDropResult(EditorDropResult result)
    {
        if (!result.accepted)
            return;
        if (result.revealTarget is GameScene scene)
        {
            m_forceOpenSceneIds.Add(scene.identity.persistentId);
        }
        else if (result.revealTarget is GameObject gameObject)
        {
            m_forceOpenIds.Add(gameObject.identity.persistentId);
        }
    }

}
