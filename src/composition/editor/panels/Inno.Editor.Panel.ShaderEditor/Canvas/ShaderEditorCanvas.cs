using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Inno.Assets.Pipeline;
using Inno.Core.Graphs;
using Inno.Editor.Graph;
using Inno.Editor.ImGui;
using Inno.Editor.Interactions;
using Inno.Native.ImGui;
using Inno.Rendering.Assets;
using Inno.Rendering.Shaders;
using Widget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using UI = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed partial class ShaderEditorCanvas(ShaderEditorDocuments owner, ShaderEditorDocuments.Draft draft)
{
    internal const string C_AREA = "panel/rendering.shader-editor";
    private const float C_WIDTH = 270f;
    private const float C_HEADER = 32f;
    private const float C_ROW = 24f;
    private GraphDocumentController Controller => owner.Controller(draft);
    private GraphCanvasState Canvas => draft.canvas;
    private Vector2 m_origin;
    private Vector2 m_size;

    internal void Draw()
    {
        DrawSaveBar();
        m_origin = UI.GetCursorScreenPos();
        m_size = Vector2.Max(UI.GetContentRegionAvail(), Vector2.One);
        RefreshPorts();
        owner.RefreshCompilation(draft);
        if (draft.frameRequested) { Frame(); draft.frameRequested = false; }
        UI.SetNextItemAllowOverlap();
        _ = UI.InvisibleButton("##shader-canvas", m_size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonMiddle | ImGuiButtonFlags.MouseButtonRight);
        bool hovered = UI.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        Vector2 mouse = UI.GetMousePos();
        if (hovered && UI.IsMouseClicked(ImGuiMouseButton.Right))
        {
            draft.menuPosition = ToGraph(mouse);
            draft.menuSearch = "";
            draft.createFromPort = null;
            GraphNodeRecord? hit = HitNode(mouse);
            draft.selectedEdge = hit is null ? HitEdge(mouse, draft.portPoints) : null;
            if (draft.selectedEdge is not null) Canvas.ClearSelection();
            if (hit is not null && !Canvas.selectedNodes.Contains(hit.id)) Canvas.SelectNodes([hit.id]);
            SetStage(hit);
        }
        if (owner.assets.TryGetFileSystemEntry(draft.path, out AssetFileEntry entry))
        {
            EditorInteraction interaction = owner.interactions.For(C_AREA, entry);
            if (UI.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)) interaction.Focus();
            _ = EditorMenuRenderer.ContextMenu("##shader-menu", interaction, ref draft.menuSearch);
        }
        Navigate(hovered);
        ImDrawListPtr draw = UI.GetWindowDrawList();
        draw.PushClipRect(m_origin, m_origin + m_size, true);
        try
        {
            draw.AddRectFilled(m_origin, m_origin + m_size, Color(0.065f, 0.071f, 0.083f));
            Grid(draw);
            Groups(draw);
            Dictionary<GraphEndpoint, Vector2> points = PortPositions();
            Edges(draw, points);
            foreach (GraphNodeRecord node in Controller.document.nodes) Node(draw, node, points);
            Pointer(hovered, points);
            if (draft.boxSelecting)
            {
                Vector2 min = Vector2.Min(draft.pointerStart, mouse), max = Vector2.Max(draft.pointerStart, mouse);
                draw.AddRectFilled(min, max, Color(0.52f, 0.37f, 0.78f, 0.13f));
                draw.AddRect(min, max, Color(0.65f, 0.47f, 0.88f));
            }
            string status = draft.readOnly ? "Read-only · copy to project to edit" : Controller.isDirty ? "Unsaved changes · Save to apply" : draft.status;
            status += " · Saved asset: " + draft.compilationStatus;
            draw.AddText(m_origin + new Vector2(12, 10), UI.GetColorU32(ImGuiCol.TextDisabled), status);
            if (draft.compilationDiagnostics.Length != 0 && mouse.Y < m_origin.Y + 32 && hovered)
                Widget.DrawTooltip(draft.compilationDiagnostics);
            if (draft.error.Length != 0) draw.AddText(m_origin + new Vector2(12, 34), Color(1f, 0.47f, 0.44f), draft.error);
        }
        finally { draw.PopClipRect(); }
        // Re-submit the canvas footprint after absolutely positioned node controls. A cursor
        // move alone can exceed ImGui's pixel-rounded item bounds at fractional UI scales.
        UI.SetCursorScreenPos(m_origin);
        UI.Dummy(m_size);
        Diagnostics();
    }

    private void DrawSaveBar()
    {
        if (UI.BeginChild("##shader-save-bar", new(0, UI.GetFrameHeight() + UI.GetStyle().ItemSpacing.Y), ImGuiChildFlags.None,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings))
        {
            UI.BeginDisabled(draft.readOnly);
            if (UI.Button("Save") && owner.assets.TryGetFileSystemEntry(draft.path, out AssetFileEntry entry))
                _ = owner.interactions.For(C_AREA, entry).Execute("shader/save");
            Widget.DrawItemTooltip("Save this shader and apply its changes (Command/Ctrl + S). Invalid graphs can be saved; rendering retains the last successful programs.");
            UI.SameLine();
            if (UI.Button("Revert")) _ = owner.interactions.documents.Revert(draft.documentId);
            Widget.DrawItemTooltip("Restore the saved shader. This draft change can be undone.");
            UI.EndDisabled();
            UI.SameLine();
            UI.TextDisabled(System.IO.Path.GetFileName(draft.path.localPath) + (Controller.isDirty ? " *" : ""));
        }
        UI.EndChild();
    }

    private void RefreshPorts()
    {
        if (draft.portRevision == Controller.revision && draft.assetRevision == owner.assets.revision && draft.typeRevision == owner.typeVersion) return;
        draft.ports.Clear();
        draft.missingPorts.Clear();
        draft.nodeErrors.Clear();
        foreach (GraphNodeRecord node in Controller.document.nodes)
        {
            if (!draft.portSnapshots.ContainsKey(node.id))
                draft.portSnapshots[node.id] = Read(node, ShaderEditorDocuments.C_PORT_SNAPSHOT, Array.Empty<ShaderPortSnapshot>());
            var ports = new List<ShaderNodePort>();
            try
            {
                if (node.definitionId == ShaderGraphDocument.outputDefinitionId)
                {
                    ShaderGraphStageSettings settings = Read(node, "settings", new ShaderGraphStageSettings());
                    ports.AddRange(settings.outputs.Select(static output => new ShaderNodePort(output.id,
                        ShaderSourceType.Atomic(output.kind == ShaderIrOutputKind.Depth ? "float" : output.kind == ShaderIrOutputKind.Varying ? "any" : "float4"),
                        GraphPortDirection.Input)));
                }
                else ports.AddRange(owner.Describe(node));
            }
            catch (Exception failure) when ((failure is InvalidOperationException or ArgumentException or FormatException) && Inno.Core.Execution.RetirementPendingException.Find(failure) is null)
            { draft.nodeErrors[node.id] = failure.Message; }
            foreach (ShaderPortSnapshot old in draft.portSnapshots[node.id])
                if (!ports.Any(port => port.id == old.id))
                {
                    ports.Add(new(old.id, old.type.CreateType(), old.direction, old.required));
                    draft.missingPorts.Add(new(node.id, new(old.id)));
                }
            foreach (GraphEdgeRecord edge in Controller.document.edges)
            {
                if (edge.input.nodeId == node.id) KeepEndpoint(edge.input, GraphPortDirection.Input);
                if (edge.output.nodeId == node.id) KeepEndpoint(edge.output, GraphPortDirection.Output);
            }
            draft.portSnapshots[node.id] = ports.Select(static port => new ShaderPortSnapshot
            { id = port.id, type = ShaderGraphType.Capture(port.type), direction = port.direction, required = port.required }).ToArray();
            draft.ports.Add(node.id, ports.ToArray());
            void KeepEndpoint(GraphEndpoint endpoint, GraphPortDirection direction)
            {
                if (ports.Any(port => port.id == endpoint.portId.value)) return;
                ports.Add(new(endpoint.portId.value, ShaderSourceType.Atomic("missing"), direction, false));
                draft.missingPorts.Add(endpoint);
            }
        }
        draft.portRevision = Controller.revision;
        draft.groups = GroupShaderNodes.Read(owner, Controller.document);
        draft.assetRevision = owner.assets.revision;
        draft.typeRevision = owner.typeVersion;
    }

    private void Navigate(bool hovered)
    {
        ImGuiIOPtr io = UI.GetIO();
        draft.navigation.Update(hovered, UI.IsMouseClicked(ImGuiMouseButton.Left), UI.IsMouseClicked(ImGuiMouseButton.Middle),
            UI.IsMouseDown(ImGuiMouseButton.Left), UI.IsMouseDown(ImGuiMouseButton.Middle), io.KeyAlt);
        if (draft.navigation.isPanning)
        {
            Canvas.PanBy(io.MouseDelta.X, io.MouseDelta.Y);
            UI.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
        }
        if (hovered && io.MouseWheel != 0)
        {
            Vector2 pivot = UI.GetMousePos() - m_origin;
            Canvas.ZoomAt(EditorPlanarNavigation.WheelFactor(io.MouseWheel), pivot.X, pivot.Y);
        }
        if (hovered && !io.WantTextInput && UI.IsKeyPressed(ImGuiKey.F, false)) Frame();
        if (!io.WantTextInput && UI.IsKeyPressed(ImGuiKey.Escape, false))
        {
            draft.dragging = draft.boxSelecting = false;
            draft.navigation.Cancel();
            draft.dragPreview.Clear();
            Canvas.CancelConnection();
            draft.createFromPort = null;
        }
    }

    private void Pointer(bool hovered, Dictionary<GraphEndpoint, Vector2> points)
    {
        if (draft.navigation.isPanning || UI.GetIO().KeyAlt) return;
        Vector2 mouse = UI.GetMousePos();
        if (hovered && UI.IsMouseClicked(ImGuiMouseButton.Left))
        {
            GraphEndpoint? hit = HitPort(mouse, points);
            if (!draft.readOnly && hit is GraphEndpoint endpoint && !draft.missingPorts.Contains(endpoint))
            {
                ShaderNodePort port = Port(endpoint);
                if (port.direction == GraphPortDirection.Output) Canvas.BeginConnection(endpoint);
                else
                {
                    GraphEdgeRecord? incoming = Controller.document.edges.FirstOrDefault(edge => edge.input == endpoint);
                    if (incoming is not null) Canvas.BeginConnection(incoming.output);
                }
                return;
            }
            GraphNodeRecord? node = HitNode(mouse);
            if (node is not null && mouse.Y <= Rect(node).min.Y + C_HEADER * Canvas.zoom)
            {
                draft.selectedEdge = null;
                if (UI.GetIO().KeyShift) Canvas.ToggleNode(node.id);
                else if (!Canvas.selectedNodes.Contains(node.id)) Canvas.SelectNodes([node.id]);
                SetStage(node);
                if (draft.readOnly) return;
                draft.dragStart.Clear();
                foreach (GraphNodeId id in Canvas.selectedNodes) draft.dragStart[id] = Controller.document.FindNode(id)!.position;
                draft.dragging = true;
                draft.pointerStart = mouse;
            }
            else if (node is null)
            {
                if (!UI.GetIO().KeyShift) Canvas.ClearSelection();
                draft.selectedEdge = HitEdge(mouse, points);
                if (draft.selectedEdge is not null) return;
                draft.boxSelecting = true;
                draft.pointerStart = mouse;
            }
        }
        if (draft.dragging && UI.IsMouseDown(ImGuiMouseButton.Left))
        {
            Vector2 delta = (mouse - draft.pointerStart) / Canvas.zoom;
            foreach ((GraphNodeId id, GraphPosition position) in draft.dragStart) draft.dragPreview[id] = new(position.x + delta.X, position.y + delta.Y);
        }
        if (!UI.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (draft.dragging && draft.dragPreview.Count != 0)
            { Controller.MoveNodes(draft.dragPreview); owner.Changed(draft); }
            if (draft.boxSelecting)
            {
                Vector2 min = Vector2.Min(draft.pointerStart, mouse), max = Vector2.Max(draft.pointerStart, mouse);
                Canvas.SelectNodes(Controller.document.nodes.Where(node => { var rect = Rect(node); return rect.max.X >= min.X && rect.min.X <= max.X && rect.max.Y >= min.Y && rect.min.Y <= max.Y; }).Select(static node => node.id));
            }
            if (Canvas.pendingConnection is GraphEndpoint source)
            {
                GraphEndpoint? target = HitPort(mouse, points);
                if (target is GraphEndpoint input && Port(input).direction == GraphPortDirection.Input
                    && !draft.missingPorts.Contains(input) && (Port(source).type.IsEquivalentTo(Port(input).type) || Port(input).type.id == "any"))
                { Controller.Connect(source, input); owner.Changed(draft); }
                else if (target is null && hovered && HitNode(mouse) is null)
                {
                    draft.createFromPort = source;
                    draft.menuPosition = ToGraph(mouse);
                    draft.menuSearch = "";
                    UI.OpenPopup("##shader-menu");
                }
                Canvas.CancelConnection();
            }
            draft.dragging = draft.boxSelecting = false;
            draft.dragPreview.Clear();
        }
    }

    private unsafe void Node(ImDrawListPtr draw, GraphNodeRecord node, Dictionary<GraphEndpoint, Vector2> points)
    {
        var rect = Rect(node);
        if (rect.max.X < m_origin.X || rect.min.X > m_origin.X + m_size.X || rect.max.Y < m_origin.Y || rect.min.Y > m_origin.Y + m_size.Y) return;
        float zoom = Canvas.zoom;
        bool selected = Canvas.selectedNodes.Contains(node.id);
        draw.AddRectFilled(rect.min + new Vector2(3, 5), rect.max + new Vector2(3, 5), Color(0, 0, 0, 0.25f), 6 * zoom);
        draw.AddRectFilled(rect.min, rect.max, Color(0.115f, 0.12f, 0.14f), 6 * zoom);
        draw.AddRectFilled(rect.min, new(rect.max.X, rect.min.Y + C_HEADER * zoom),
            node.definitionId == ShaderGraphDocument.outputDefinitionId ? Color(0.25f, 0.19f, 0.34f) : Color(0.16f, 0.17f, 0.20f), 6 * zoom);
        draw.AddRect(rect.min, rect.max, selected ? Color(0.65f, 0.47f, 0.88f) : Color(0.26f, 0.27f, 0.31f), 6 * zoom, ImDrawFlags.None, selected ? 2 : 1);
        draw.AddText(UI.GetFont(), UI.GetFontSize() * zoom, rect.min + new Vector2(10, 7) * zoom, UI.GetColorU32(ImGuiCol.Text), Title(node));
        foreach (ShaderNodePort port in draft.ports[node.id])
        {
            Vector2 point = points[new(node.id, new(port.id))];
            bool missing = draft.missingPorts.Contains(new(node.id, new(port.id)));
            draw.AddCircleFilled(point, 5 * zoom, missing ? Color(0.95f, 0.35f, 0.3f) : Color(0.60f, 0.48f, 0.84f));
            string label = port.id;
            float x = port.direction == GraphPortDirection.Input ? rect.min.X + 12 * zoom : rect.max.X - (UI.CalcTextSize(label).X + 12) * zoom;
            draw.AddText(UI.GetFont(), UI.GetFontSize() * zoom, new(x, point.Y - 8 * zoom), UI.GetColorU32(ImGuiCol.Text), label);
            if (Vector2.DistanceSquared(point, UI.GetMousePos()) <= 64)
                Widget.DrawTooltip(port.type.id + (missing ? " · missing port; reconnect explicitly" : ""));
        }
        if (zoom >= 0.55f)
        {
            int rows = Rows(node);
            UI.SetCursorScreenPos(rect.min + new Vector2(12, C_HEADER + rows * C_ROW + 8) * zoom);
            UI.PushID(node.id.value);
            ImGuiStylePtr style = UI.GetStyle();
            float baseFontSize = UI.GetFontSize() / MathF.Max(0.01f, style.FontScaleMain * style.FontScaleDpi);
            UI.PushFont(UI.GetFont(), baseFontSize * zoom);
            UI.PushStyleVar(ImGuiStyleVar.FramePadding, style.FramePadding * zoom);
            UI.PushStyleVar(ImGuiStyleVar.ItemSpacing, style.ItemSpacing * zoom);
            UI.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            try
            {
                if (UI.BeginChild("##controls", new((C_WIDTH - 24) * zoom, ControlHeight(node) * zoom), ImGuiChildFlags.None,
                    ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
                {
                    UI.PushItemWidth(-1);
                    UI.BeginDisabled(draft.readOnly);
                    try { Controls(node); }
                    catch (Exception failure) when ((failure is System.IO.IOException or InvalidOperationException or ArgumentException or FormatException) && Inno.Core.Execution.RetirementPendingException.Find(failure) is null)
                    { draft.nodeErrors[node.id] = failure.Message; }
                    finally { UI.EndDisabled(); UI.PopItemWidth(); }
                }
                UI.EndChild();
            }
            finally { UI.PopStyleVar(3); UI.PopFont(); UI.PopID(); }
        }
        if (draft.nodeErrors.TryGetValue(node.id, out string? error))
        {
            draw.AddCircleFilled(rect.min + new Vector2(C_WIDTH - 14, 16) * zoom, 4 * zoom, Color(1, 0.4f, 0.35f));
            if (Contains(rect, UI.GetMousePos())) Widget.DrawTooltip(error);
        }
    }

    private void Grid(ImDrawListPtr draw)
    {
        float spacing = 32 * Canvas.zoom;
        for (float x = ((Canvas.pan.x % spacing) + spacing) % spacing; x < m_size.X; x += spacing)
            draw.AddLine(m_origin + new Vector2(x, 0), m_origin + new Vector2(x, m_size.Y), Color(0.105f, 0.115f, 0.135f));
        for (float y = ((Canvas.pan.y % spacing) + spacing) % spacing; y < m_size.Y; y += spacing)
            draw.AddLine(m_origin + new Vector2(0, y), m_origin + new Vector2(m_size.X, y), Color(0.105f, 0.115f, 0.135f));
    }
    private void Groups(ImDrawListPtr draw)
    {
        foreach (ShaderCanvasGroup group in draft.groups)
        {
            Vector2 min = new(float.MaxValue), max = new(float.MinValue);
            foreach (string id in group.nodes)
                if (Controller.document.FindNode(new(id)) is GraphNodeRecord node)
                { var rect = Rect(node); min = Vector2.Min(min, rect.min); max = Vector2.Max(max, rect.max); }
            if (min.X == float.MaxValue) continue;
            min -= new Vector2(20, 40) * Canvas.zoom;
            max += new Vector2(20) * Canvas.zoom;
            draw.AddRectFilled(min, max, Color(0.16f, 0.13f, 0.20f, 0.35f), 8);
            draw.AddRect(min, max, Color(0.30f, 0.25f, 0.37f), 8);
            draw.AddText(min + new Vector2(12, 10), UI.GetColorU32(ImGuiCol.TextDisabled), group.title);
        }
    }
    private Dictionary<GraphEndpoint, Vector2> PortPositions()
    {
        Dictionary<GraphEndpoint, Vector2> points = draft.portPoints;
        points.Clear();
        foreach (GraphNodeRecord node in Controller.document.nodes)
        {
            var rect = Rect(node);
            int left = 0, right = 0;
            foreach (ShaderNodePort port in draft.ports[node.id])
            {
                int row = port.direction == GraphPortDirection.Input ? left++ : right++;
                points[new(node.id, new(port.id))] = new(port.direction == GraphPortDirection.Input ? rect.min.X : rect.max.X,
                    rect.min.Y + (C_HEADER + C_ROW * (row + 0.5f)) * Canvas.zoom);
            }
        }
        return points;
    }
    private void Edges(ImDrawListPtr draw, Dictionary<GraphEndpoint, Vector2> points)
    {
        foreach (GraphEdgeRecord edge in Controller.document.edges)
            if (points.TryGetValue(edge.output, out Vector2 a) && points.TryGetValue(edge.input, out Vector2 b)) Curve(a, b, edge.id == draft.selectedEdge);
        if (Canvas.pendingConnection is GraphEndpoint pending && points.TryGetValue(pending, out Vector2 start)) Curve(start, UI.GetMousePos());
        void Curve(Vector2 a, Vector2 b, bool selected = false)
        {
            float tangent = MathF.Max(36, MathF.Abs(b.X - a.X) * 0.45f);
            draw.AddBezierCubic(a, a + new Vector2(tangent, 0), b - new Vector2(tangent, 0), b,
                selected ? Color(0.87f, 0.72f, 1f) : Color(0.59f, 0.46f, 0.82f), selected ? 3 : 2);
        }
    }
    private GraphEdgeId? HitEdge(Vector2 mouse, Dictionary<GraphEndpoint, Vector2> points)
    {
        foreach (GraphEdgeRecord edge in Controller.document.edges)
        {
            if (!points.TryGetValue(edge.output, out Vector2 a) || !points.TryGetValue(edge.input, out Vector2 b)) continue;
            float tangent = MathF.Max(36, MathF.Abs(b.X - a.X) * 0.45f);
            Vector2 c = a + new Vector2(tangent, 0), d = b - new Vector2(tangent, 0), previous = a;
            for (int segment = 1; segment <= 32; segment++)
            {
                float t = segment / 32f, s = 1 - t;
                Vector2 next = s * s * s * a + 3 * s * s * t * c + 3 * s * t * t * d + t * t * t * b;
                Vector2 line = next - previous;
                float projection = Math.Clamp(Vector2.Dot(mouse - previous, line) / MathF.Max(0.001f, line.LengthSquared()), 0, 1);
                if (Vector2.DistanceSquared(mouse, previous + projection * line) < 36) return edge.id;
                previous = next;
            }
        }
        return null;
    }
    private (Vector2 min, Vector2 max) Rect(GraphNodeRecord node)
    {
        GraphPosition position = draft.dragPreview.GetValueOrDefault(node.id, node.position);
        Vector2 min = m_origin + new Vector2(Canvas.pan.x, Canvas.pan.y) + new Vector2(position.x, position.y) * Canvas.zoom;
        return (min, min + new Vector2(C_WIDTH, C_HEADER + Rows(node) * C_ROW + ControlHeight(node) + 20) * Canvas.zoom);
    }
    private float ControlHeight(GraphNodeRecord node) => owner.drawers?.ContentHeight(node.definitionId) ?? node.definitionId switch
    {
        "inno.shader.stage-input" => draft.expandedPreviews.Contains(node.id) ? 440 : 300,
        "inno.shader.source" => 100,
        ShaderGraphDocument.outputDefinitionId => 140,
        _ => 110
    };
    private int Rows(GraphNodeRecord node) => Math.Max(draft.ports[node.id].Count(static port => port.direction == GraphPortDirection.Input), draft.ports[node.id].Count(static port => port.direction == GraphPortDirection.Output));
    private GraphNodeRecord? HitNode(Vector2 point) => Controller.document.nodes.Reverse().FirstOrDefault(node => Contains(Rect(node), point));
    private GraphEndpoint? HitPort(Vector2 point, Dictionary<GraphEndpoint, Vector2> ports)
    {
        foreach ((GraphEndpoint endpoint, Vector2 position) in ports)
            if (Vector2.DistanceSquared(point, position) <= MathF.Pow(MathF.Max(8, 7 * Canvas.zoom), 2)) return endpoint;
        return null;
    }
    private ShaderNodePort Port(GraphEndpoint endpoint) => draft.ports[endpoint.nodeId].Single(port => port.id == endpoint.portId.value);
    private GraphPosition ToGraph(Vector2 point) => new((point.X - m_origin.X - Canvas.pan.x) / Canvas.zoom, (point.Y - m_origin.Y - Canvas.pan.y) / Canvas.zoom);
    private static bool Contains((Vector2 min, Vector2 max) rect, Vector2 point) => point.X >= rect.min.X && point.Y >= rect.min.Y && point.X <= rect.max.X && point.Y <= rect.max.Y;
    private static uint Color(float r, float g, float b, float a = 1) => UI.ColorConvertFloat4ToU32(new(r, g, b, a));
    private T Read<T>(GraphNodeRecord node, string key, T value) => ShaderGraphDocument.Read(node, key, value, owner.serialization, owner.context);
    private void Set<T>(GraphNodeRecord node, string key, T value, bool continuous = false)
        => SetEncoded(node, key, ShaderGraphDocument.Encode(value, owner.serialization, owner.context), continuous);
    private void SetEncoded(GraphNodeRecord node, string key, GraphSerializedValue value, bool continuous)
    {
        if (UI.IsItemActivated()) draft.valueGesture = Guid.NewGuid().ToString("N");
        if (key == "settings" && node.definitionId == "inno.shader.stage-input")
            Controller.ReplaceDocument(ShaderGraphBindings.ChangeInput(Controller.document, node.id,
                ShaderGraphDocument.Decode<ShaderGraphInputSettings>(value, owner.serialization, owner.context), owner.serialization, owner.context),
                "Edit Shader Input", continuous ? draft.valueGesture : null);
        else Controller.SetNodeValue(node.id, key, value, continuous ? draft.valueGesture : null);
        owner.Changed(draft);
    }
    private void SetStage(GraphNodeRecord? node)
    {
        if (node is null) return;
        string stage = node.definitionId == ShaderGraphDocument.outputDefinitionId ? node.id.value : Read(node, "stage", "");
        if (stage.Length != 0) draft.activeStage = new(stage);
    }
    private void Frame()
    {
        GraphNodeRecord[] selection = Controller.document.nodes.Where(node => Canvas.selectedNodes.Count == 0 || Canvas.selectedNodes.Contains(node.id)).ToArray();
        if (selection.Length == 0) return;
        float minX = selection.Min(static node => node.position.x), minY = selection.Min(static node => node.position.y);
        float maxX = selection.Max(static node => node.position.x + C_WIDTH);
        float maxY = selection.Max(node => node.position.y + C_HEADER + Rows(node) * C_ROW + ControlHeight(node) + 20);
        float scale = Math.Clamp(MathF.Min(MathF.Max(1, m_size.X - 80) / MathF.Max(1, maxX - minX), MathF.Max(1, m_size.Y - 80) / MathF.Max(1, maxY - minY)), 0.1f, 1f);
        Canvas.SetViewport(new(m_size.X / 2 - (minX + maxX) / 2 * scale, m_size.Y / 2 - (minY + maxY) / 2 * scale), scale);
    }
    private string Title(GraphNodeRecord node) => node.definitionId == ShaderGraphDocument.outputDefinitionId
        ? Read(node, "settings", new ShaderGraphStageSettings()).stage + " Output"
        : node.definitionId.Replace("inno.shader.", "", StringComparison.Ordinal).Replace('-', ' ');
}
