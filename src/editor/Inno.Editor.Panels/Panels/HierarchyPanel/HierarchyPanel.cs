using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Core.Identity;
using Inno.Editor.Core;
using Inno.Editor.HotKeys;
using Inno.Editor.ImGui;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panels;

/// <summary>
/// Edits the active scene object hierarchy.
/// </summary>
public sealed class HierarchyPanel : EditorPanel
{
    private const string C_SCENE_OBJECT_PAYLOAD = "INNO_SCENE_OBJECT";
    private const string C_SCENE_PAYLOAD = "INNO_SCENE";
    private const nuint C_NAME_BUFFER_SIZE = 512;

    private static readonly Vector4 s_sceneRowColor = new(28f / 255f, 26f / 255f, 25f / 255f, 1f);
    private static readonly Vector4 s_inactiveTextColor = new(0.52f, 0.52f, 0.54f, 1f);

    private readonly HierarchySelection m_selection = new();
    private readonly HierarchySceneCommands m_sceneCommands = new();
    private readonly HierarchyDragDrop m_dragDrop;
    private readonly HashSet<Guid> m_forceOpenIds = [];
    private readonly HashSet<Guid> m_forceOpenSceneIds = [];
    private readonly HashSet<Guid> m_drawnIds = [];
    private readonly HashSet<Guid> m_initializedSceneIds = [];
    private Guid? m_renamingId;
    private Guid? m_pendingDeleteId;
    private string m_renameBuffer = string.Empty;
    private bool m_focusRename;
    private bool m_isFocused;
    private int m_visibleRowIndex;
    private IDisposable? m_renameHotKeyRegistration;
    private IDisposable? m_deleteHotKeyRegistration;

    /// <summary>
    /// Creates the hierarchy panel.
    /// </summary>
    public HierarchyPanel()
        : base("scene.hierarchy", "Hierarchy")
    {
        m_dragDrop = new HierarchyDragDrop(m_selection);
    }

    /// <inheritdoc />
    public override void OnAttach(EditorContext context)
    {
        m_renameHotKeyRegistration = context.hotKeys.Register(
            EditorHotKeyCommands.Rename,
            () => BeginRenameFromSelection(context),
            () => CanRenameSelection(context));
        m_deleteHotKeyRegistration = context.hotKeys.Register(
            EditorHotKeyCommands.Delete,
            () => RequestDeleteFromSelection(context),
            () => CanDeleteSelection(context));
    }

    /// <inheritdoc />
    public override void OnDetach(EditorContext context)
    {
        m_renameHotKeyRegistration?.Dispose();
        m_deleteHotKeyRegistration?.Dispose();
        m_renameHotKeyRegistration = null;
        m_deleteHotKeyRegistration = null;
    }

    /// <inheritdoc />
    public override void OnRender(EditorContext context)
    {
        m_drawnIds.Clear();
        m_visibleRowIndex = 0;
        m_isFocused = NativeImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        m_selection.Prune(context);

        NativeImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(3f, 2f));
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, 1f));
        try
        {
            IReadOnlyList<GameScene> scenes = context.scenes;
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

        ApplyPendingDelete(context);
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
                backgroundColor = s_sceneRowColor,
                suppressHoverHighlight = false
            });

        if (result.isClicked || result.isDoubleClicked)
            context.selection.Select(scene);

        _ = ImGuiWidget.DragDropSource(
            C_SCENE_PAYLOAD,
            sceneId,
            () => NativeImGui.TextUnformatted(scene.name));

        bool sceneDelivered = ImGuiWidget.DragDropTarget(
            C_SCENE_PAYLOAD,
            out Guid droppedSceneId,
            out bool isScenePreviewing,
            drawDefaultHighlight: false);
        if (isScenePreviewing)
            m_dragDrop.DrawSceneDropPreview(result);
        if (sceneDelivered)
        {
            GameScene? movedScene = m_dragDrop.ReorderScene(scene, droppedSceneId, result);
            if (movedScene is not null)
                context.selection.Select(movedScene);
        }

        if (ImGuiWidget.DragDropTarget<Guid>(C_SCENE_OBJECT_PAYLOAD, out Guid droppedId))
        {
            GameObject? moved = m_dragDrop.MoveToSceneRoot(scene, droppedId);
            if (moved is not null)
            {
                m_forceOpenSceneIds.Add(sceneId);
                context.selection.Select(moved);
            }
        }

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
            () => DrawRowContent(gameObject),
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
            BeginRename(gameObject);
        }

        Guid payload = gameObject.identity.persistentId;
        _ = ImGuiWidget.DragDropSource(
            C_SCENE_OBJECT_PAYLOAD,
            payload,
            () => NativeImGui.TextUnformatted(gameObject.name));
        bool delivered = ImGuiWidget.DragDropTarget(
            C_SCENE_OBJECT_PAYLOAD,
            out Guid droppedId,
            out bool isPreviewing,
            drawDefaultHighlight: false);
        if (isPreviewing)
        {
            m_dragDrop.DrawDropPreview(result);
        }

        if (delivered)
        {
            GameObject? moved = m_dragDrop.ApplyDrop(
                scene,
                droppedId,
                gameObject,
                result,
                m_forceOpenIds);
            if (moved is not null)
                context.selection.Select(moved);
        }

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

    private void DrawRowContent(GameObject gameObject)
    {
        string id = gameObject.identity.persistentId.ToString("N");
        bool dimmed = !gameObject.activeInHierarchy;
        if (dimmed)
        {
            NativeImGui.PushStyleColor(ImGuiCol.Text, s_inactiveTextColor);
        }

        if (m_renamingId == gameObject.identity.persistentId)
        {
            ImGuiWidget.IconText(ImGuiIcon.Cube, string.Empty, false);
            NativeImGui.SameLine(0f, 0f);
            float visibilityWidth = GetVisibilityButtonWidth();
            float renameWidth = MathF.Max(48f, NativeImGui.GetContentRegionAvail().X - visibilityWidth - 8f);
            InlineRenameResult renameResult = ImGuiWidget.InlineRename(
                $"hierarchy_{id}",
                ref m_renameBuffer,
                ref m_focusRename,
                C_NAME_BUFFER_SIZE,
                renameWidth);
            if (renameResult == InlineRenameResult.Cancel)
            {
                EndRename();
            }
            else if (renameResult == InlineRenameResult.Commit)
            {
                gameObject.name = string.IsNullOrWhiteSpace(m_renameBuffer)
                    ? "GameObject"
                    : m_renameBuffer.Trim();
                EndRename();
            }
        }
        else
        {
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
        if (!ImGuiWidget.BeginContextMenu($"##scene_context_{id}"))
        {
            return;
        }

        context.selection.Select(scene);
        bool isActive = ReferenceEquals(scene, SceneManager.activeScene);
        if (NativeImGui.MenuItem("Set Active Scene", string.Empty, false, !isActive))
        {
            SceneManager.SetActiveScene(scene);
        }

        if (NativeImGui.MenuItem("Create Empty"))
        {
            GameObject created = scene.CreateObject();
            m_forceOpenSceneIds.Add(((IIdentityObject)scene).GetIdentity().persistentId);
            context.selection.Select(created);
        }

        NativeImGui.Separator();
        if (NativeImGui.MenuItem("Create Scene"))
        {
            GameScene createdScene = m_sceneCommands.Create(context);
            m_forceOpenSceneIds.Add(createdScene.identity.persistentId);
        }

        NativeImGui.Separator();
        bool canDelete = context.scenes.Count > 1;
        if (NativeImGui.MenuItem("Delete", "Delete", false, canDelete))
        {
            m_pendingDeleteId = scene.identity.persistentId;
        }

        ImGuiWidget.EndContextMenu();
    }

    private void DrawObjectContextMenu(EditorContext context, GameObject gameObject, string id)
    {
        if (!ImGuiWidget.BeginContextMenu($"##hierarchy_context_{id}"))
        {
            return;
        }

        context.selection.Select(gameObject);
        if (NativeImGui.MenuItem("Create Empty Child"))
        {
            GameObject child = gameObject.scene.CreateObject();
            child.GetComponent<Transform>().SetParent(gameObject.GetComponent<Transform>());
            m_forceOpenIds.Add(gameObject.identity.persistentId);
            context.selection.Select(child);
            BeginRename(child);
        }

        if (NativeImGui.MenuItem("Rename", "F2"))
        {
            BeginRename(gameObject);
        }

        if (NativeImGui.MenuItem("Delete", "Delete"))
        {
            m_pendingDeleteId = gameObject.identity.persistentId;
        }

        ImGuiWidget.EndContextMenu();
    }

    private void DrawBlankArea(EditorContext context)
    {
        Vector2 available = NativeImGui.GetContentRegionAvail();
        Vector2 size = new(MathF.Max(1f, available.X), MathF.Max(24f, available.Y));
        _ = NativeImGui.InvisibleButton("##hierarchy_blank", size);
        if (NativeImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            context.selection.Clear();
        }

        if (ImGuiWidget.DragDropTarget<Guid>(C_SCENE_OBJECT_PAYLOAD, out Guid droppedId))
        {
            GameObject? moved = m_dragDrop.MoveToSceneRoot(context.scene, droppedId);
            if (moved is not null)
                context.selection.Select(moved);
        }

        if (!ImGuiWidget.BeginContextMenu("##hierarchy_blank_context"))
        {
            return;
        }

        if (NativeImGui.MenuItem("Create Scene"))
        {
            GameScene createdScene = m_sceneCommands.Create(context);
            m_forceOpenSceneIds.Add(createdScene.identity.persistentId);
        }

        NativeImGui.Separator();
        if (NativeImGui.MenuItem("Create Empty"))
        {
            GameObject created = context.scene.CreateObject();
            m_forceOpenSceneIds.Add(((IIdentityObject)context.scene).GetIdentity().persistentId);
            context.selection.Select(created);
            BeginRename(created);
        }

        ImGuiWidget.EndContextMenu();
    }

    private bool CanRenameSelection(EditorContext context)
        => m_isFocused &&
           !NativeImGui.GetIO().WantTextInput &&
           m_renamingId is null &&
           context.selection.TryGet(out GameObject? gameObject) &&
           gameObject.isRuntimeValid;

    private bool CanDeleteSelection(EditorContext context)
    {
        if (!m_isFocused || NativeImGui.GetIO().WantTextInput || m_renamingId is not null)
            return false;
        if (context.selection.TryGet(out GameObject? gameObject))
            return gameObject.isRuntimeValid;
        return context.selection.TryGet(out GameScene? scene) &&
               scene.isLoaded &&
               !scene.isDestroyed &&
               context.scenes.Count > 1;
    }

    private void BeginRenameFromSelection(EditorContext context)
    {
        if (context.selection.TryGet(out GameObject? gameObject))
            BeginRename(gameObject);
    }

    private void RequestDeleteFromSelection(EditorContext context)
    {
        if (context.selection.TryGet(out GameObject? gameObject))
        {
            m_pendingDeleteId = gameObject.identity.persistentId;
            return;
        }
        if (context.selection.TryGet(out GameScene? scene))
            m_pendingDeleteId = scene.identity.persistentId;
    }

    private void ApplyPendingDelete(EditorContext context)
    {
        if (m_pendingDeleteId is not Guid persistentId)
            return;
        m_pendingDeleteId = null;
        GameScene? scene = IdentityManager.Get<GameScene>(persistentId);
        bool deleted = scene is not null && scene.isLoaded
            ? m_sceneCommands.Delete(context, scene)
            : m_selection.DeleteObject(context, persistentId);
        if (deleted && scene is not null)
        {
            m_initializedSceneIds.Remove(persistentId);
            m_forceOpenSceneIds.Remove(persistentId);
        }
        if (deleted && m_renamingId == persistentId)
            EndRename();
    }

    private void BeginRename(GameObject gameObject)
    {
        m_renamingId = gameObject.identity.persistentId;
        m_renameBuffer = gameObject.name;
        m_focusRename = true;
    }

    private void EndRename()
    {
        m_renamingId = null;
        m_renameBuffer = string.Empty;
        m_focusRename = false;
    }

}
