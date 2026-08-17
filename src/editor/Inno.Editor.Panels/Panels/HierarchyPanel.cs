using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Editor.Core;
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
    private const nuint C_NAME_BUFFER_SIZE = 512;

    private static readonly Vector4 s_sceneRowColor = new(28f / 255f, 26f / 255f, 25f / 255f, 1f);
    private static readonly Vector4 s_inactiveTextColor = new(0.52f, 0.52f, 0.54f, 1f);

    private readonly HashSet<Guid> m_forceOpenIds = [];
    private readonly HashSet<Guid> m_drawnIds = [];
    private readonly HashSet<Guid> m_initializedSceneIds = [];
    private Guid? m_renamingId;
    private Guid? m_pendingDeleteId;
    private string m_renameBuffer = string.Empty;
    private bool m_focusRename;
    private int m_visibleRowIndex;

    /// <summary>
    /// Creates the hierarchy panel.
    /// </summary>
    public HierarchyPanel()
        : base("scene.hierarchy", "Hierarchy")
    {
    }

    /// <inheritdoc />
    public override void OnRender(EditorContext context)
    {
        m_drawnIds.Clear();
        m_visibleRowIndex = 0;
        PruneSelection(context);
        HandleKeyboardShortcuts(context);

        NativeImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(3f, 0f));
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
        if (m_initializedSceneIds.Add(sceneId))
        {
            ImGuiWidget.SetNextTreeNodeOpen(true);
        }

        bool selected = context.selection.TryGet(out GameScene? selectedScene) &&
            ReferenceEquals(selectedScene, scene);
        TreeNodeResult result = ImGuiWidget.TreeNode(
            $"scene_{id}",
            () => ImGuiWidget.IconText(
                ImGuiIcon.LayerGroup,
                scene.name,
                ReferenceEquals(scene, SceneManager.activeScene)),
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

        if (ImGuiWidget.DragDropTarget<Guid>(C_SCENE_OBJECT_PAYLOAD, out Guid droppedId))
        {
            MoveToSceneRoot(scene, droppedId);
        }

        DrawSceneContextMenu(context, scene, id);
        if (!result.isOpen)
        {
            return;
        }

        IReadOnlyList<GameObject> roots = GetRootObjects(scene);
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
            out bool isPreviewing);
        if (isPreviewing)
        {
            DrawDropPreview(result);
        }

        if (delivered)
        {
            ApplyDrop(scene, droppedId, gameObject, result);
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

    private static void DrawSceneContextMenu(EditorContext context, GameScene scene, string id)
    {
        if (!ImGuiWidget.BeginContextMenu($"##scene_context_{id}"))
        {
            return;
        }

        bool isActive = ReferenceEquals(scene, SceneManager.activeScene);
        if (NativeImGui.MenuItem("Set Active Scene", string.Empty, false, !isActive))
        {
            SceneManager.SetActiveScene(scene);
        }

        if (NativeImGui.MenuItem("Create Empty"))
        {
            GameObject created = scene.CreateObject();
            context.selection.Select(created);
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
            MoveToSceneRoot(context.scene, droppedId);
        }

        if (!ImGuiWidget.BeginContextMenu("##hierarchy_blank_context"))
        {
            return;
        }

        if (NativeImGui.MenuItem("Create Empty"))
        {
            GameObject created = context.scene.CreateObject();
            context.selection.Select(created);
            BeginRename(created);
        }

        ImGuiWidget.EndContextMenu();
    }

    private void ApplyDrop(
        GameScene scene,
        Guid droppedId,
        GameObject target,
        in TreeNodeResult result)
    {
        GameObject? dropped = IdentityManager.Get<GameObject>(droppedId);
        if (dropped is null || ReferenceEquals(dropped, target) ||
            !dropped.isRuntimeValid || !ReferenceEquals(dropped.scene, scene))
        {
            return;
        }

        try
        {
            Transform droppedTransform = dropped.GetComponent<Transform>();
            Transform targetTransform = target.GetComponent<Transform>();
            float height = MathF.Max(1f, result.max.Y - result.min.Y);
            float relativeY = (NativeImGui.GetMousePos().Y - result.min.Y) / height;
            if (relativeY is >= 0.25f and <= 0.75f)
            {
                droppedTransform.SetParent(targetTransform);
                droppedTransform.SetSiblingIndex(targetTransform.children.Count - 1);
                m_forceOpenIds.Add(target.identity.persistentId);
                return;
            }

            Transform? targetParent = targetTransform.parent;
            droppedTransform.SetParent(targetParent);
            int sourceIndex = droppedTransform.siblingIndex;
            int targetIndex = targetTransform.siblingIndex;
            if (sourceIndex < targetIndex)
            {
                targetIndex--;
            }

            int desiredIndex = targetIndex + (relativeY > 0.75f ? 1 : 0);
            droppedTransform.SetSiblingIndex(desiredIndex);
        }
        catch (InvalidOperationException exception)
        {
            Log.Warn("Hierarchy drop was rejected: {0}", exception.Message);
        }
    }

    private static void MoveToSceneRoot(GameScene scene, Guid droppedId)
    {
        GameObject? dropped = IdentityManager.Get<GameObject>(droppedId);
        if (dropped is null || !dropped.isRuntimeValid || !ReferenceEquals(dropped.scene, scene))
        {
            return;
        }

        try
        {
            Transform transform = dropped.GetComponent<Transform>();
            transform.SetParent(null);
            transform.SetSiblingIndex(GetRootObjects(scene).Count - 1);
        }
        catch (InvalidOperationException exception)
        {
            Log.Warn("Hierarchy scene root drop was rejected: {0}", exception.Message);
        }
    }

    private static void DrawDropPreview(in TreeNodeResult result)
    {
        float height = MathF.Max(1f, result.max.Y - result.min.Y);
        float relativeY = (NativeImGui.GetMousePos().Y - result.min.Y) / height;
        if (relativeY < 0.25f)
        {
            ImGuiWidget.InsertionLine(result.min.X, result.max.X, result.min.Y);
        }
        else if (relativeY > 0.75f)
        {
            ImGuiWidget.InsertionLine(result.min.X, result.max.X, result.max.Y);
        }
    }

    private void HandleKeyboardShortcuts(EditorContext context)
    {
        if (!NativeImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) ||
            !context.selection.TryGet(out GameObject? gameObject) ||
            !gameObject.isRuntimeValid)
        {
            return;
        }

        if (m_renamingId is null && NativeImGui.IsKeyPressed(ImGuiKey.F2))
        {
            BeginRename(gameObject);
        }

        if (m_renamingId is null && NativeImGui.IsKeyPressed(ImGuiKey.Delete))
        {
            m_pendingDeleteId = gameObject.identity.persistentId;
        }
    }

    private static void PruneSelection(EditorContext context)
    {
        if (context.selection.TryGet(out GameScene? selectedScene) &&
            (!selectedScene.isLoaded || !ContainsScene(context.scenes, selectedScene)))
        {
            context.selection.Clear();
            return;
        }
        if (context.selection.TryGet(out GameObject? gameObject) &&
            (!gameObject.isRuntimeValid || !ContainsScene(context.scenes, gameObject.scene)))
        {
            context.selection.Clear();
        }
    }

    private void ApplyPendingDelete(EditorContext context)
    {
        if (m_pendingDeleteId is not Guid persistentId)
        {
            return;
        }

        m_pendingDeleteId = null;
        GameObject? gameObject = IdentityManager.Get<GameObject>(persistentId);
        if (gameObject is null || !gameObject.isRuntimeValid || !ContainsScene(context.scenes, gameObject.scene))
        {
            return;
        }

        _ = gameObject.scene.DestroyObject(gameObject);
        if (context.selection.TryGet(out GameObject? selected) && ReferenceEquals(selected, gameObject))
        {
            context.selection.Clear();
        }

        if (m_renamingId == persistentId)
        {
            EndRename();
        }
    }

    private static bool ContainsScene(IReadOnlyList<GameScene> scenes, GameScene scene)
    {
        for (int i = 0; i < scenes.Count; i++)
        {
            if (ReferenceEquals(scenes[i], scene))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<GameObject> GetRootObjects(GameScene scene)
    {
        IReadOnlyList<GameObject> objects = scene.GetObjects();
        var roots = new List<GameObject>(objects.Count);
        for (int i = 0; i < objects.Count; i++)
        {
            if (objects[i].transform.parent is null)
                roots.Add(objects[i]);
        }
        roots.Sort(static (left, right) => left.transform.siblingIndex.CompareTo(right.transform.siblingIndex));
        return roots;
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
