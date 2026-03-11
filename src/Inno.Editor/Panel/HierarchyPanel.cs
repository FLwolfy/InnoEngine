using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.ECS;
using Inno.Core.Math;
using Inno.Editor.Core;
using Inno.Editor.GUI;

using ImGuiNET;
using Inno.Core.Events;
using Inno.ImGui;
using ImGuiNet = ImGuiNET.ImGui;

using BranchInfo = (float, uint);

namespace Inno.Editor.Panel;

public class HierarchyPanel : EditorPanel
{
    public override string title => "Hierarchy";

    private readonly Queue<Action> m_pendingGuiUpdateAction = new();
    private readonly Stack<BranchInfo> m_branch = new();
    private int m_row;

    internal HierarchyPanel() { }

    internal override void OnGUI()
    {
        m_row = 0;
        m_branch.Clear();

        ImGuiNet.BeginChild("##HierarchyScroll", new Vector2(0, 0), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);

        // Split draw channels: 0 = background (stripes), 1 = normal content (tree/items/drag highlight)
        var dl = ImGuiNet.GetWindowDrawList();
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);

        var activeScene = SceneManager.GetActiveScene();

        if (activeScene != null)
        {
            DrawSceneRoot();
            foreach (var obj in activeScene.GetAllRootGameObjects())
                DrawGameObject(obj);
        }

        HandleMenu();
        while (m_pendingGuiUpdateAction.Count > 0) m_pendingGuiUpdateAction.Dequeue().Invoke();

        // Merge channels back
        dl.ChannelsMerge();

        ImGuiNet.EndChild();
    }

    private void HandleMenu()
    {
        var io = ImGuiNet.GetIO();
        if (!ImGuiNet.IsAnyItemHovered() && ImGuiNet.IsWindowHovered() && io.MouseClicked[(int)Input.MouseButton.Right])
            ImGuiNet.OpenPopup("HierarchyContextMenu");

        if (!ImGuiNet.BeginPopup("HierarchyContextMenu")) return;

        if (ImGuiNet.BeginMenu("Create"))
        {
            if (ImGuiNet.MenuItem("GameObject"))
                m_pendingGuiUpdateAction.Enqueue(() =>
                {
                    var go = new GameObject("New GameObject");
                    EditorManager.selection.Select(go);
                });
            ImGuiNet.EndMenu();
        }

        ImGuiNet.EndPopup();
    }

    private void DrawSceneRoot()
    {
        DrawStripe();
        ImGuiNet.Text("[ Scene Root ]");

        if (!ImGuiNet.BeginDragDropTarget()) return;

        if (ImGuiHost.TryAcceptDragPayload(EditorPayloadType.GAMEOBJECT_PAYLOAD, out GameObject obj))
        {
            m_pendingGuiUpdateAction.Enqueue(() => obj?.transform.SetParent(null));
        }

        ImGuiNet.EndDragDropTarget();
    }

    private void DrawGameObject(GameObject obj, bool isLastChild = false)
    {
        var selection = EditorManager.selection;
        bool selected = selection.IsSelected(obj);
        bool hasChildren = obj.transform.children.Count > 0;

        DrawStripe();

        var flags = (hasChildren
                ? ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick
                : ImGuiTreeNodeFlags.Leaf)
            | ImGuiTreeNodeFlags.SpanFullWidth;

        if (selected) flags |= ImGuiTreeNodeFlags.Selected;

        bool open = ImGuiNet.TreeNodeEx($"###{obj.id}", flags);

        var rMin = ImGuiNet.GetItemRectMin();
        var rMax = ImGuiNet.GetItemRectMax();
        float yMid = (rMin.Y + rMax.Y) * 0.5f;

        DrawBranchLinesForThisRow(rMin, rMax, yMid, isLastChild, hasChildren);

        if (ImGuiNet.BeginPopupContextItem($"Popup_{obj.id}"))
        {
            if (ImGuiNet.MenuItem("Delete"))
                m_pendingGuiUpdateAction.Enqueue(() => obj.scene.UnregisterGameObject(obj));
            ImGuiNet.EndPopup();
        }

        if (ImGuiNet.IsItemClicked((int)Input.MouseButton.Left)) selection.Select(obj);

        if (ImGuiNet.BeginDragDropSource())
        {
            ImGuiHost.SetDragPayload(EditorPayloadType.GAMEOBJECT_PAYLOAD, obj);
            ImGuiNet.Text($"Dragging {obj.name}");
            ImGuiNet.EndDragDropSource();
        }

        if (ImGuiNet.BeginDragDropTarget())
        {
            if (ImGuiHost.TryAcceptDragPayload(EditorPayloadType.GAMEOBJECT_PAYLOAD, out GameObject payloadObj))
            {
                m_pendingGuiUpdateAction.Enqueue(() => payloadObj?.transform.SetParent(obj.transform));
            }
            ImGuiNet.EndDragDropTarget();
        }

        bool isCamera = obj.GetAllComponents().Any(c => c.GetType().IsAssignableTo(typeof(Camera)));
        ImGuiNet.SameLine();
        EditorImGuiEx.DrawIconAndText(isCamera ? ImGuiIcon.Camera : ImGuiIcon.Cube, obj.name);

        if (hasChildren && open)
        {
            var style = ImGuiNet.GetStyle();
            var offsetX = ImGuiNet.GetFontSize() * 0.5f;
            float arrowX = rMin.X + style.FramePadding.X + style.IndentSpacing * m_branch.Count + offsetX;
            uint lineCol = ImGuiNet.GetColorU32(ImGuiCol.Separator);

            m_branch.Push((arrowX, lineCol));

            int childCount = obj.transform.children.Count;
            for (int i = 0; i < childCount; i++)
                DrawGameObject(obj.transform.children[i].gameObject, i == childCount - 1);

            m_branch.Pop();
        }

        if (open) ImGuiNet.TreePop();
    }
    
    private void DrawBranchLinesForThisRow(Vector2 rMin, Vector2 rMax, float yMid, bool isLastChild, bool hasChildren)
    {
        if (m_branch.Count == 0) return;

        var dl = ImGuiNet.GetWindowDrawList();
        var style = ImGuiNet.GetStyle();

        var top = m_branch.Peek();
        float y2 = isLastChild ? yMid : rMax.y;
        dl.AddLine(new Vector2(top.Item1, rMin.y), new Vector2(top.Item1, y2), top.Item2);

        float offsetX = hasChildren ? 0 : ImGuiNet.GetFontSize() * 0.8f;
        float thisIndentX = rMin.x + style.FramePadding.X + style.IndentSpacing * m_branch.Count + offsetX;
        dl.AddLine(new Vector2(top.Item1, yMid), new Vector2(thisIndentX, yMid), top.Item2);
    }

    private void DrawStripe()
    {
        var style = ImGuiNet.GetStyle();
        var bg = style.Colors[(int)ImGuiCol.WindowBg];
        float delta = (m_row++ & 1) == 0 ? 0.018f : -0.010f;

        var col = new Vector4(
            MathHelper.Clamp(bg.X + delta, 0, 1f),
            MathHelper.Clamp(bg.Y + delta, 0, 1f),
            MathHelper.Clamp(bg.Z + delta, 0, 1f),
            bg.W
        );

        var winPos = ImGuiNet.GetWindowPos();
        var winSize = ImGuiNet.GetWindowSize();
        var p = ImGuiNet.GetCursorScreenPos();
        float hh = ImGuiNet.GetFrameHeight();

        var dl = ImGuiNet.GetWindowDrawList();

        // Draw stripe on background channel so it never covers drag/hover highlight
        dl.ChannelsSetCurrent(0);
        dl.AddRectFilled(
            new Vector2(winPos.X, p.Y),
            new Vector2(winPos.X + winSize.X, p.Y + hh),
            ImGuiNet.GetColorU32(col)
        );
        dl.ChannelsSetCurrent(1);
    }
}
