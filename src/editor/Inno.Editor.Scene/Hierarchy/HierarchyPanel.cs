using Inno.Editor.Scene.DragDrop;

using Inno.Editor.Scene;

using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Core.Identity;
using Inno.Editor.Core;
using Inno.Editor.Core.Commands;
using Inno.Editor.Core.DragDrop;
using Inno.Editor.Scene.Inspection;
using Inno.Editor.Scene.Workspace;
using Inno.Editor.Core.Menus;
using Inno.Editor.Core.Panels;
using Inno.Editor.ImGui;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Scene.Hierarchy;

/// <summary>
/// Edits the active scene object hierarchy.
/// </summary>
[EditorPanel("scene.hierarchy", "Hierarchy", order: 100)]
public sealed class HierarchyPanel : EditorPanel
{
    private const nuint C_NAME_BUFFER_SIZE = 512;

    private readonly EditorSceneWorkspace m_workspace;
    private readonly HierarchySelection m_selection;
    private readonly HierarchyDropVisual m_dropVisual = new();
    private readonly HashSet<Guid> m_forceOpenIds = [];
    private readonly HashSet<Guid> m_forceOpenSceneIds = [];
    private readonly HashSet<Guid> m_drawnIds = [];
    private readonly HashSet<Guid> m_initializedSceneIds = [];
    private EditorRenameSession? m_activeRenameSession;
    private bool m_focusRename;
    private int m_visibleRowIndex;

    /// <summary>
    /// Creates the hierarchy panel.
    /// </summary>
    internal HierarchyPanel(EditorSceneWorkspace workspace)
    {
        m_workspace = workspace;
        m_selection = new HierarchySelection(workspace);
    }

    /// <inheritdoc />
    public override void Draw(EditorContext context)
    {
        m_drawnIds.Clear();
        m_visibleRowIndex = 0;
        m_selection.Prune(context);
        RevealSelection(context);

        NativeImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, ImGuiWidget.style.hierarchyItemSpacing);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, ImGuiWidget.style.compactFramePadding);
        try
        {
            IReadOnlyList<GameScene> scenes = m_workspace.scenes;
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
            ImGuiWidget.SetNextTreeNodeOpen(true);
        }

        bool selected = context.selection.TryGet(out GameScene? selectedScene) &&
            ReferenceEquals(selectedScene, scene);
        TreeNodeResult result = ImGuiWidget.TreeNode(
            $"scene_{id}",
            () => m_selection.DrawSceneRowContent(context, scene),
            new TreeNodeOptions
            {
                isLeaf = false,
                selected = selected,
                showBackground = true,
                backgroundColor = EditorPalette.hierarchySceneRow,
                suppressHoverHighlight = false
            });

        if (result.isClicked || result.isDoubleClicked)
            context.selection.Select(scene);

        _ = EditorDragDropRenderer.Source(
            new EditorDragContext(
                context,
                typeof(SceneSurface.HierarchyScene),
                new EditorDragData(scene, scene.name, () => scene.isLoaded)),
            () => NativeImGui.TextUnformatted(scene.name));

        EditorDropPlacement scenePlacement = m_dropVisual.GetScenePlacement(result, NativeImGui.GetMousePos().Y);
        EditorDropWidgetResult sceneDrop = EditorDragDropRenderer.Target(
            context,
            typeof(SceneSurface.HierarchyScene),
            new HierarchySceneDropTarget(scene),
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

        NativeImGui.TreePop();
    }

    private void DrawObject(EditorContext context, GameScene scene, GameObject gameObject)
    {
        if (!gameObject.isRuntimeValid || !m_drawnIds.Add(gameObject.identity.persistentId))
        {
            return;
        }

        Transform transform = gameObject.GetComponent<Transform>();
        bool selected = context.selection.TryGet(out GameObject? selectedObject)
            && ReferenceEquals(selectedObject, gameObject);
        string id = gameObject.identity.persistentId.ToString("N");
        if (m_forceOpenIds.Remove(gameObject.identity.persistentId))
        {
            ImGuiWidget.SetNextTreeNodeOpen(true);
        }

        TreeNodeResult result = ImGuiWidget.TreeNode(
            $"hierarchy_{id}",
            () => DrawRowContent(context, gameObject),
            new TreeNodeOptions
            {
                selected = selected,
                isLeaf = transform.children.Count == 0,
                showBackground = true,
                backgroundColor = m_visibleRowIndex % 2 == 0
                    ? EditorPalette.collectionRow
                    : EditorPalette.collectionRowAlternate
            });
        m_visibleRowIndex++;

        if (result.isClicked || result.isDoubleClicked)
        {
            context.selection.Select(gameObject);
        }

        if (result.isDoubleClicked)
        {
            _ = context.Execute(
                EditorActionIds.Rename,
                typeof(SceneSurface.HierarchyObject),
                gameObject);
        }

        _ = EditorDragDropRenderer.Source(
            new EditorDragContext(
                context,
                typeof(SceneSurface.HierarchyObject),
                new EditorDragData(gameObject, gameObject.name, () => gameObject.isRuntimeValid)),
            () => NativeImGui.TextUnformatted(gameObject.name));
        EditorDropPlacement placement = m_dropVisual.GetObjectPlacement(result, NativeImGui.GetMousePos().Y);
        EditorDropWidgetResult objectDrop = EditorDragDropRenderer.Target(
            context,
            typeof(SceneSurface.HierarchyObject),
            new HierarchyObjectDropTarget(gameObject),
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

        NativeImGui.TreePop();
    }

    private void DrawRowContent(EditorContext context, GameObject gameObject)
    {
        string id = gameObject.identity.persistentId.ToString("N");
        bool dimmed = !gameObject.activeInHierarchy;
        if (dimmed)
        {
            NativeImGui.PushStyleColor(ImGuiCol.Text, EditorPalette.hierarchyInactiveText);
        }

        EditorRenameSession? renameSession = m_workspace.rename;
        if (renameSession is not null && ReferenceEquals(renameSession.target, gameObject))
        {
            if (!ReferenceEquals(m_activeRenameSession, renameSession))
            {
                m_activeRenameSession = renameSession;
                m_focusRename = true;
            }
            ImGuiWidget.IconText(ImGuiIcon.Cube, string.Empty, false);
            NativeImGui.SameLine(0f, 0f);
            float visibilityWidth = GetVisibilityButtonWidth();
            float renameWidth = MathF.Max(
                ImGuiWidget.style.hierarchyRenameMinimumWidth,
                NativeImGui.GetContentRegionAvail().X -
                visibilityWidth -
                ImGuiWidget.style.hierarchyRenameTrailingGap);
            string renameBuffer = renameSession.buffer;
            InlineRenameResult renameResult = ImGuiWidget.InlineRename(
                $"hierarchy_{id}",
                ref renameBuffer,
                ref m_focusRename,
                C_NAME_BUFFER_SIZE,
                renameWidth);
            renameSession.buffer = renameBuffer;
            if (renameResult == InlineRenameResult.Cancel)
            {
                m_workspace.CancelRename();
            }
            else if (renameResult == InlineRenameResult.Commit)
            {
                _ = renameSession.Commit();
            }
        }
        else
        {
            if (ReferenceEquals(m_activeRenameSession, renameSession) || renameSession is null)
                m_activeRenameSession = null;
            ImGuiWidget.IconText(ImGuiIcon.Cube, gameObject.name, false);
        }

        if (dimmed)
        {
            NativeImGui.PopStyleColor();
        }

        DrawVisibilityButton(gameObject, id);
    }

    private static void DrawVisibilityButton(GameObject gameObject, string id)
    {
        string icon = gameObject.activeSelf ? ImGuiIcon.Eye : ImGuiIcon.EyeSlash;
        float buttonWidth = GetVisibilityButtonWidth();
        float right = NativeImGui.GetWindowPos().X
            + NativeImGui.GetWindowSize().X
            - NativeImGui.GetStyle().WindowPadding.X
            - buttonWidth;
        NativeImGui.SameLine();
        Vector2 cursor = NativeImGui.GetCursorScreenPos();
        NativeImGui.SetCursorScreenPos(new Vector2(MathF.Max(cursor.X, right), cursor.Y));
        if (ImGuiWidget.IconButton($"hierarchy_visibility_{id}", icon,
                gameObject.activeSelf ? "Deactivate" : "Activate"))
        {
            gameObject.SetActive(!gameObject.activeSelf);
        }
    }

    private static float GetVisibilityButtonWidth() => ImGuiWidget.GetIconButtonSize().X;

    private void DrawSceneContextMenu(EditorContext context, GameScene scene, string id)
    {
        if (NativeImGui.IsItemClicked(ImGuiMouseButton.Right))
            context.selection.Select(scene);
        _ = EditorMenuRenderer.ContextMenu(
            $"##scene_context_{id}",
            new EditorMenuContext(context, typeof(SceneSurface.HierarchyScene), scene));
    }

    private void DrawObjectContextMenu(EditorContext context, GameObject gameObject, string id)
    {
        if (NativeImGui.IsItemClicked(ImGuiMouseButton.Right))
            context.selection.Select(gameObject);
        _ = EditorMenuRenderer.ContextMenu(
            $"##hierarchy_context_{id}",
            new EditorMenuContext(context, typeof(SceneSurface.HierarchyObject), gameObject));
    }

    private void DrawBlankArea(EditorContext context)
    {
        Vector2 available = NativeImGui.GetContentRegionAvail();
        Vector2 size = new(
            MathF.Max(1f, available.X),
            MathF.Max(ImGuiWidget.style.hierarchyBlankMinimumHeight, available.Y));
        _ = NativeImGui.InvisibleButton("##hierarchy_blank", size);
        if (NativeImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            context.selection.Clear();
        }

        EditorDropWidgetResult drop = EditorDragDropRenderer.Target(
            context,
            typeof(SceneSurface.HierarchyBlank),
            new HierarchySceneDropTarget(m_workspace.activeScene),
            EditorDropPlacement.Into);
        ApplyDropResult(drop.result);

        _ = EditorMenuRenderer.ContextMenu(
            "##hierarchy_blank_context",
            new EditorMenuContext(context, typeof(SceneSurface.HierarchyBlank), m_workspace.activeScene));
    }

    private void RevealSelection(EditorContext context)
    {
        if (context.selection.TryGet(out GameScene? scene))
        {
            m_forceOpenSceneIds.Add(scene.identity.persistentId);
            return;
        }
        if (!context.selection.TryGet(out GameObject? gameObject) || !gameObject.isRuntimeValid)
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
