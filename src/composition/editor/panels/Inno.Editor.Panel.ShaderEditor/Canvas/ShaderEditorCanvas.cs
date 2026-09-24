using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Inno.Assets.Pipeline;
using Inno.Core.Graphs;
using Inno.Editor.Graph;
using Inno.Editor.ImGui;
using Inno.Editor.Interactions;
using Inno.Editor.Rendering;
using Inno.Editor.Shaders;
using Inno.Native.ImGui;
using Inno.Rendering.Assets;
using Inno.Rendering.Shaders;
using Widget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using ImGuiApi = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed partial class ShaderEditorCanvas(ShaderEditorDocuments owner, ShaderEditorDocuments.Draft draft)
{
    private readonly record struct PortHandle(GraphEndpoint endpoint, GraphPortDirection direction);

    internal const string C_AREA = "panel/rendering.shader-editor";
    private const string C_NODE_WIDTH = "editor.width";
    private const float C_DEFAULT_WIDTH = 270f;
    private const float C_MINIMUM_WIDTH = 180f;
    private const float C_MAXIMUM_WIDTH = 800f;
    private const float C_HEADER = 32f;
    private const float C_ROW = 24f;
    private const float C_RESIZE_HANDLE = 14f;
    private GraphDocumentController Controller => owner.Controller(draft);
    private GraphCanvasState Canvas => draft.canvas;
    private Vector2 m_origin;
    private Vector2 m_size;

    internal void Draw()
    {
        DrawHeader(owner, draft);
        m_origin = ImGuiApi.GetCursorScreenPos();
        m_size = Vector2.Max(ImGuiApi.GetContentRegionAvail(), Vector2.One);
        RefreshPorts();
        owner.RefreshCompilation(draft);
        if (draft.frameRequested) { Frame(); draft.frameRequested = false; }
        Dictionary<PortHandle, Vector2> points = PortPositions();
        ImGuiApi.SetNextItemAllowOverlap();
        _ = ImGuiApi.InvisibleButton("##shader-canvas", m_size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonMiddle | ImGuiButtonFlags.MouseButtonRight);
        bool hovered = ImGuiApi.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        Vector2 mouse = ImGuiApi.GetMousePos();
        if (hovered && ImGuiApi.IsMouseClicked(ImGuiMouseButton.Right))
        {
            draft.menuPosition = ToGraph(mouse);
            draft.createFromPort = null;
            ShaderCanvasGroup? hitGroup = HitGroupHeader(mouse);
            if (hitGroup is ShaderCanvasGroup group)
            {
                draft.selectedGroupId = group.id;
                Canvas.SelectNodes(group.nodes.Select(static id => new GraphNodeId(id)));
            }
            GraphNodeRecord? hit = hitGroup is null ? HitNode(mouse) : null;
            draft.selectedEdge = hit is null ? HitEdge(mouse, points) : null;
            if (draft.selectedEdge is not null) Canvas.ClearSelection();
            if (hit is not null && !Canvas.selectedNodes.Contains(hit.id)) Canvas.SelectNodes([hit.id]);
            SetStage(hit);
        }
        if (owner.assets.TryGetFileSystemEntry(draft.path, out AssetFileEntry entry))
        {
            EditorInteraction interaction = owner.interactions.For(C_AREA, entry);
            if (ImGuiApi.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)) interaction.Focus();
            _ = EditorMenuRenderer.ContextMenu("##shader-menu", interaction);
        }
        Navigate(hovered);
        ImDrawListPtr draw = ImGuiApi.GetWindowDrawList();
        draw.PushClipRect(m_origin, m_origin + m_size, true);
        try
        {
            draw.AddRectFilled(m_origin, m_origin + m_size, Color(0.065f, 0.071f, 0.083f));
            Grid(draw);
            Groups(draw);
            Edges(draw, points);
            foreach (GraphNodeRecord node in Controller.document.nodes) Node(draw, node, points);
            Pointer(hovered, points);
            if (draft.boxSelecting)
            {
                Vector2 min = Vector2.Min(draft.pointerStart, mouse), max = Vector2.Max(draft.pointerStart, mouse);
                draw.AddRectFilled(min, max, Color(0.52f, 0.37f, 0.78f, EditorPalette.opacitySubtle));
                draw.AddRect(min, max, Color(0.65f, 0.47f, 0.88f));
            }
            string status = draft.readOnly ? "Read-only · copy to project to edit" : Controller.isDirty ? "Unsaved changes · Save to apply" : draft.status;
            status += " · Saved asset: " + draft.compilationStatus;
            draw.AddText(m_origin + new Vector2(12, 10), ImGuiApi.GetColorU32(ImGuiCol.TextDisabled), status);
            if (draft.compilationDiagnostics.Length != 0 && mouse.Y < m_origin.Y + 32 && hovered)
                Widget.DrawTooltip(draft.compilationDiagnostics);
            if (draft.error.Length != 0) draw.AddText(m_origin + new Vector2(12, 34), Color(1f, 0.47f, 0.44f), draft.error);
        }
        finally { draw.PopClipRect(); }
        // Re-submit the canvas footprint after absolutely positioned node controls. A cursor
        // move alone can exceed ImGui's pixel-rounded item bounds at fractional UI scales.
        ImGuiApi.SetCursorScreenPos(m_origin);
        ImGuiApi.Dummy(m_size);
        SynchronizeInspection();
    }

    private void SynchronizeInspection()
    {
        if (Canvas.selectedNodes.SequenceEqual(draft.inspectedNodes)) return;
        draft.inspectedNodes = Canvas.selectedNodes.ToArray();
        owner.interactions.SetSelection(new ShaderInspectionSelection(draft.id, draft.inspectedNodes));
    }

    internal void DrawInspector(Inno.Editor.Inspection.InspectionDrawContext inspection, IReadOnlyList<GraphNodeId> selected)
    {
        m_inspection = inspection;
        try { DrawInspectorContents(selected); }
        finally { m_inspection = null; }
    }

    private void DrawInspectorContents(IReadOnlyList<GraphNodeId> selected)
    {
        RefreshPorts();
        bool graphNode = ShaderGraphNodes.IsNodeGraph(Controller.document);
        if (!graphNode)
        {
            if (Widget.SectionHeader("Preview", "The preview uses the current draft without publishing it to Scene or Game views."))
            {
                var preview = owner.Preview(draft);
                if (preview is null)
                {
                    Widget.Hint("Run Check to build a preview for the current draft.");
                }
                else if (preview.state == EditorShaderCompilationState.Failed)
                {
                    Widget.Hint("Shader check failed. See Console.");
                }
                else if (preview.state == EditorShaderCompilationState.Succeeded && preview.artifact is not null
                    && owner.interactions.TryGetModule<Inno.Editor.Shaders.ShaderPreviews>(out var images) && images is not null)
                {
                    var shader = owner.assets.Load<Inno.Rendering.ShaderAsset>(draft.path);
                    images.Draw(draft.id, new Inno.Rendering.MaterialAsset { shader = shader }, preview,
                        MathF.Max(1f, MathF.Min(256f, ImGuiApi.GetContentRegionAvail().X)));
                }
            }
        }
        else
        {
            if (Widget.SectionHeader("Graph Node", "Function Inputs and Function Outputs independently define the reusable typed interface. Check validates the interface; callers validate the inlined computation."))
            {
                try
                {
                    ShaderGraphNodeInterface nodeInterface = ShaderGraphNodes.ReadInterface(Controller.document, owner.serialization, owner.context);
                    InspectorRow("graph-node.summary", "Interface", () => Widget.WrappedText(
                        $"{nodeInterface.inputs.Length} input(s) · {nodeInterface.outputs.Length} output(s) · {nodeInterface.kind}"));
                }
                catch (Exception failure) when (failure is InvalidOperationException or ArgumentException or FormatException or NotSupportedException)
                {
                    Widget.Hint(failure.Message);
                }
            }
        }
        if (selected.Count == 0)
        {
            if (Widget.SectionHeader(graphNode ? "Node Definition" : "Shader",
                graphNode ? "Select Function Inputs or Function Outputs to edit this reusable node's public interface."
                    : "Target and public interface belong to this Shader, not to any Material override."))
            {
                if (!graphNode) DrawTarget();
                if (Controller.document.metadata.ContainsKey(ShaderGraphDocument.definitionKey))
                {
                    var definition = ShaderGraphDocument.ReadDefinition(Controller.document, owner.serialization, owner.context);
                    foreach (var property in definition.properties)
                        InspectorRow("definition." + property.id.value, property.displayName,
                            () => Widget.WrappedText(property.type + " · " + property.bindingOwner));
                }
                owner.RefreshCompilation(draft);
                InspectorRow("compilation", "Compilation", () => Widget.WrappedText(draft.compilationStatus));
            }
            return;
        }
        GraphNodeRecord[] nodes = selected.Select(Controller.document.FindNode).OfType<GraphNodeRecord>().ToArray();
        if (nodes.Length != selected.Count) Widget.Hint("Some selected nodes are unavailable. Stable identities are retained.");
        if (nodes.Length == 0) return;
        if (nodes.Any(node => node.definitionId != nodes[0].definitionId))
        { Widget.Hint("Select nodes of the same kind to edit common settings."); return; }
        m_inspectionNodes = nodes;
        ImGuiApi.PushID(draft.id.ToString("N"));
        ImGuiApi.PushID(nodes[0].id.value);
        ImGuiApi.BeginDisabled(draft.readOnly);
        try
        {
            if (Widget.SectionHeader(Title(nodes[0]), "Inputs, defaults and output configuration are edited here. Changes remain in the Shader draft until Save."))
            {
                if (nodes.Length > 1) Widget.Hint("Editing " + nodes.Length + " nodes · differing values are replaced together");
                Controls(nodes[0]);
            }
            ShaderNodePort[] inputs = draft.ports[nodes[0].id].Where(port => port.direction == GraphPortDirection.Input).ToArray();
            if (inputs.Length != 0 && Widget.SectionHeader("Inputs", "Connected inputs show their source. Optional inputs use an automatic zero; required inputs must be connected in the graph."))
            {
                foreach (ShaderNodePort port in inputs)
                    DrawInputDefault(nodes[0], port);
            }
        }
        finally { ImGuiApi.EndDisabled(); ImGuiApi.PopID(); ImGuiApi.PopID(); m_inspectionNodes = null; }
    }

    private GraphNodeRecord[]? m_inspectionNodes;
    private Inno.Editor.Inspection.InspectionDrawContext? m_inspection;

    private static void InspectorRow(string id, string label, Action draw)
        => Widget.PropertyRow("shader." + id, label, draw);

    internal static void DrawHeader(ShaderEditorDocuments owner, ShaderEditorDocuments.Draft? draft)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Widget.HeaderSurface("##shader-header", () =>
        {
            ImGuiApi.SetNextItemWidth(-1f);
            string title = draft is null
                ? "Select Shader"
                : System.IO.Path.GetFileName(draft.path.localPath) + (owner.Controller(draft).isDirty ? " *" : "");
            if (Widget.BeginBoundedCombo("##shader-header-select", title))
            {
                try
                {
                    foreach (AssetFileEntry candidate in owner.assets.GetFileSystemEntries(includeDirectories: false)
                                 .Where(static candidate => candidate.extension.Equals(".ishader", StringComparison.OrdinalIgnoreCase))
                                 .OrderBy(static candidate => candidate.assetPath.ToString(), StringComparer.Ordinal))
                    {
                        bool selected = draft is not null && candidate.assetPath == draft.path;
                        if (ImGuiApi.Selectable(candidate.assetPath.ToString(), selected))
                        {
                            owner.interactions.SetSelection(candidate);
                            _ = owner.Open(candidate);
                        }
                        if (selected) ImGuiApi.SetItemDefaultFocus();
                    }
                }
                finally { ImGuiApi.EndCombo(); }
            }
            bool noDocument = draft is null;
            ImGuiApi.BeginDisabled(noDocument || draft!.readOnly);
            if (ImGuiApi.Button("Save") && draft is not null && owner.assets.TryGetFileSystemEntry(draft.path, out AssetFileEntry entry))
                _ = owner.interactions.For(C_AREA, entry).Execute("shader/save");
            Widget.DrawItemTooltip("Save this shader and apply its changes (Command/Ctrl + S). Invalid graphs can be saved; rendering retains the last successful programs.");
            ImGuiApi.SameLine();
            if (ImGuiApi.Button("Revert") && draft is not null) _ = owner.interactions.documents.Revert(draft.documentId);
            Widget.DrawItemTooltip("Restore the saved shader. This draft change can be undone.");
            ImGuiApi.EndDisabled();
            ImGuiApi.SameLine();
            ImGuiApi.BeginDisabled(noDocument);
            if (ImGuiApi.Button("Format") && draft is not null && owner.assets.TryGetFileSystemEntry(draft.path, out AssetFileEntry formatEntry))
                _ = owner.interactions.For(C_AREA, formatEntry).Execute("shader/format");
            Widget.DrawItemTooltip("Arrange the graph from inputs on the left to outputs on the right. This changes only authoring positions and is undoable.");
            ImGuiApi.SameLine();
            if (ImGuiApi.Button("Check") && draft is not null && owner.assets.TryGetFileSystemEntry(draft.path, out AssetFileEntry checkEntry))
                _ = owner.interactions.For(C_AREA, checkEntry).Execute("shader/check");
            Widget.DrawItemTooltip("Compile-check the current draft without saving or publishing it, and show diagnostics with source locations.");
            ImGuiApi.EndDisabled();
        }, spanWindowPadding: true);
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
        owner.PublishNodeDiagnostics(draft);
    }

    private void Navigate(bool hovered)
    {
        ImGuiIOPtr io = ImGuiApi.GetIO();
        draft.navigation.Update(hovered, ImGuiApi.IsMouseClicked(ImGuiMouseButton.Left), ImGuiApi.IsMouseClicked(ImGuiMouseButton.Middle),
            ImGuiApi.IsMouseDown(ImGuiMouseButton.Left), ImGuiApi.IsMouseDown(ImGuiMouseButton.Middle), io.KeyAlt);
        if (draft.navigation.isPanning)
        {
            Canvas.PanBy(io.MouseDelta.X, io.MouseDelta.Y);
            ImGuiApi.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
        }
        if (hovered && io.MouseWheel != 0)
        {
            Vector2 pivot = ImGuiApi.GetMousePos() - m_origin;
            Canvas.ZoomAt(EditorPlanarNavigation.WheelFactor(io.MouseWheel), pivot.X, pivot.Y);
        }
        if (hovered && !io.WantTextInput && ImGuiApi.IsKeyPressed(ImGuiKey.F, false)) Frame();
        if (!io.WantTextInput && ImGuiApi.IsKeyPressed(ImGuiKey.Escape, false))
        {
            draft.dragging = draft.boxSelecting = false;
            draft.resizingNode = null;
            draft.navigation.Cancel();
            draft.dragPreview.Clear();
            Canvas.CancelConnection();
            draft.createFromPort = null;
        }
    }

    private void Pointer(bool hovered, Dictionary<PortHandle, Vector2> points)
    {
        if (draft.navigation.isPanning || ImGuiApi.GetIO().KeyAlt) return;
        Vector2 mouse = ImGuiApi.GetMousePos();
        if (draft.resizingNode is GraphNodeId resizing)
        {
            ImGuiApi.SetMouseCursor(ImGuiMouseCursor.ResizeNwse);
            if (ImGuiApi.IsMouseDown(ImGuiMouseButton.Left))
            {
                draft.resizePreviewWidth = Math.Clamp(
                    draft.resizeStartWidth + (mouse.X - draft.pointerStart.X) / Canvas.zoom,
                    C_MINIMUM_WIDTH,
                    C_MAXIMUM_WIDTH);
            }
            else
            {
                GraphNodeRecord? resizedNode = Controller.document.FindNode(resizing);
                if (resizedNode is not null && MathF.Abs(NodeWidth(resizedNode, includePreview: false) - draft.resizePreviewWidth) > 0.01f)
                {
                    GraphDocument resizedGraph = Controller.document.Clone();
                    resizedGraph.FindNode(resizing)!.SetValue(
                        C_NODE_WIDTH,
                        ShaderGraphDocument.Encode(draft.resizePreviewWidth, owner.serialization, owner.context));
                    Controller.ReplaceDocument(resizedGraph, "Resize Shader Node");
                    owner.Changed(draft);
                }
                draft.resizingNode = null;
            }
            return;
        }
        GraphNodeRecord? resizeTarget = !draft.readOnly && hovered ? HitResizeHandle(mouse) : null;
        if (resizeTarget is not null)
            ImGuiApi.SetMouseCursor(ImGuiMouseCursor.ResizeNwse);
        if (hovered && ImGuiApi.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (resizeTarget is not null)
            {
                draft.selectedGroupId = "";
                draft.selectedEdge = null;
                Canvas.SelectNodes([resizeTarget.id]);
                draft.resizingNode = resizeTarget.id;
                draft.resizeStartWidth = NodeWidth(resizeTarget, includePreview: false);
                draft.resizePreviewWidth = draft.resizeStartWidth;
                draft.pointerStart = mouse;
                return;
            }
            PortHandle? hit = HitPort(mouse, points);
            if (!draft.readOnly && hit is PortHandle handle && !draft.missingPorts.Contains(handle.endpoint))
            {
                Canvas.BeginConnection(handle.endpoint, handle.direction);
                return;
            }
            GraphNodeRecord? node = HitNode(mouse);
            ShaderCanvasGroup? group = node is null ? HitGroupHeader(mouse) : null;
            if (group is ShaderCanvasGroup selectedGroup)
            {
                draft.selectedEdge = null;
                draft.selectedGroupId = selectedGroup.id;
                Canvas.SelectNodes(selectedGroup.nodes.Select(static id => new GraphNodeId(id)));
                if (!draft.readOnly)
                {
                    draft.dragStart.Clear();
                    foreach (GraphNodeId id in Canvas.selectedNodes)
                        if (Controller.document.FindNode(id) is GraphNodeRecord member) draft.dragStart[id] = member.position;
                    draft.dragging = draft.dragStart.Count != 0;
                    draft.pointerStart = mouse;
                }
                return;
            }
            if (node is not null && mouse.Y <= Rect(node).min.Y + C_HEADER * Canvas.zoom)
            {
                draft.selectedGroupId = "";
                draft.selectedEdge = null;
                if (ImGuiApi.GetIO().KeyShift) Canvas.ToggleNode(node.id);
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
                draft.selectedGroupId = "";
                if (!ImGuiApi.GetIO().KeyShift) Canvas.ClearSelection();
                draft.selectedEdge = HitEdge(mouse, points);
                if (draft.selectedEdge is not null) return;
                draft.boxSelecting = true;
                draft.pointerStart = mouse;
            }
        }
        if (draft.dragging && ImGuiApi.IsMouseDown(ImGuiMouseButton.Left))
        {
            Vector2 delta = (mouse - draft.pointerStart) / Canvas.zoom;
            foreach ((GraphNodeId id, GraphPosition position) in draft.dragStart) draft.dragPreview[id] = new(position.x + delta.X, position.y + delta.Y);
        }
        if (!ImGuiApi.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (draft.dragging && draft.dragPreview.Count != 0)
            { Controller.MoveNodes(draft.dragPreview); owner.Changed(draft); }
            if (draft.boxSelecting)
            {
                Vector2 min = Vector2.Min(draft.pointerStart, mouse), max = Vector2.Max(draft.pointerStart, mouse);
                Canvas.SelectNodes(Controller.document.nodes.Where(node => { var rect = Rect(node); return rect.max.X >= min.X && rect.min.X <= max.X && rect.max.Y >= min.Y && rect.min.Y <= max.Y; }).Select(static node => node.id));
            }
            if (Canvas.pendingConnection is GraphEndpoint sourceEndpoint
                && Canvas.pendingConnectionDirection is GraphPortDirection sourceDirection)
            {
                var source = new PortHandle(sourceEndpoint, sourceDirection);
                PortHandle? target = HitPort(mouse, points);
                if (target is PortHandle destination
                    && destination.direction != source.direction
                    && !draft.missingPorts.Contains(destination.endpoint))
                {
                    PortHandle output = source.direction == GraphPortDirection.Output ? source : destination;
                    PortHandle input = source.direction == GraphPortDirection.Input ? source : destination;
                    ShaderNodePort outputPort = Port(output);
                    ShaderNodePort inputPort = Port(input);
                    if (outputPort.type.IsEquivalentTo(inputPort.type) || inputPort.type.id == "any")
                    {
                        Controller.Connect(output.endpoint, input.endpoint);
                        owner.Changed(draft);
                    }
                }
                else if (source.direction == GraphPortDirection.Output
                    && target is null && hovered && HitNode(mouse) is null)
                {
                    draft.createFromPort = source.endpoint;
                    draft.menuPosition = ToGraph(mouse);
                    ImGuiApi.OpenPopup("##shader-menu");
                }
                Canvas.CancelConnection();
            }
            draft.dragging = draft.boxSelecting = false;
            draft.dragPreview.Clear();
        }
    }

    private unsafe void Node(ImDrawListPtr draw, GraphNodeRecord node, Dictionary<PortHandle, Vector2> points)
    {
        var rect = Rect(node);
        if (rect.max.X < m_origin.X || rect.min.X > m_origin.X + m_size.X || rect.max.Y < m_origin.Y || rect.min.Y > m_origin.Y + m_size.Y) return;
        float zoom = Canvas.zoom;
        bool selected = Canvas.selectedNodes.Contains(node.id);
        draw.AddRectFilled(rect.min + new Vector2(3, 5), rect.max + new Vector2(3, 5),
            Color(0, 0, 0, EditorPalette.opacityMuted), 6 * zoom);
        draw.AddRectFilled(rect.min, rect.max, Color(0.115f, 0.12f, 0.14f), 6 * zoom);
        Vector4 headerColor = NodeHeaderColor(node);
        draw.AddRectFilled(rect.min, new(rect.max.X, rect.min.Y + C_HEADER * zoom),
            ImGuiApi.ColorConvertFloat4ToU32(headerColor), 6 * zoom, ImDrawFlags.RoundCornersTop);
        draw.AddRect(rect.min, rect.max, selected ? Color(0.65f, 0.47f, 0.88f) : Color(0.26f, 0.27f, 0.31f), 6 * zoom, ImDrawFlags.None, selected ? 2 : 1);
        draw.AddText(ImGuiApi.GetFont(), ImGuiApi.GetFontSize() * zoom, rect.min + new Vector2(10, 7) * zoom, ImGuiApi.GetColorU32(ImGuiCol.Text), Title(node));
        ShaderNodePort[] nodePorts = draft.ports[node.id];
        bool splitPorts = UsesSplitPortLayout(nodePorts);
        float divider = (rect.min.X + rect.max.X) * 0.5f;
        float bodyTop = rect.min.Y + C_HEADER * zoom;
        if (splitPorts)
        {
            float bodyPadding = 7f * zoom;
            draw.AddLine(
                new Vector2(divider, bodyTop + bodyPadding),
                new Vector2(divider, rect.max.Y - bodyPadding),
                Color(0.62f, 0.63f, 0.69f, EditorPalette.opacityFaint));
        }
        foreach (ShaderNodePort port in nodePorts)
        {
            var handle = new PortHandle(new(node.id, new(port.id)), port.direction);
            Vector2 point = points[handle];
            bool missing = draft.missingPorts.Contains(new(node.id, new(port.id)));
            bool optional = port.direction == GraphPortDirection.Input && !port.required;
            uint portColor = missing ? Color(0.95f, 0.35f, 0.3f) : Color(0.60f, 0.48f, 0.84f);
            if (missing || !optional)
                draw.AddCircleFilled(point, 5 * zoom, portColor);
            else
            {
                draw.AddCircleFilled(point, 5 * zoom, portColor);
                draw.AddCircleFilled(point, 2.75f * zoom, Color(0.115f, 0.12f, 0.14f));
            }
            float halfPadding = 12f * zoom;
            float portAreaWidth = splitPorts
                ? (rect.max.X - rect.min.X) * 0.5f
                : rect.max.X - rect.min.X;
            float availableWidth = MathF.Max(1f, portAreaWidth - halfPadding * 2f);
            string label = FitPortLabel(port.id, availableWidth, zoom);
            float labelWidth = ImGuiApi.CalcTextSize(label).X * zoom;
            bool input = port.direction == GraphPortDirection.Input;
            float x = input ? rect.min.X + halfPadding : rect.max.X - labelWidth - halfPadding;
            float clipLeft = splitPorts && !input ? divider + 1f : rect.min.X + 1f;
            float clipRight = splitPorts && input ? divider - 1f : rect.max.X - 1f;
            Vector2 clipMin = new(clipLeft, bodyTop);
            Vector2 clipMax = new(clipRight, rect.max.Y);
            draw.PushClipRect(clipMin, clipMax, true);
            draw.AddText(ImGuiApi.GetFont(), ImGuiApi.GetFontSize() * zoom, new(x, point.Y - 8 * zoom),
                ImGuiApi.ColorConvertFloat4ToU32(optional ? EditorPalette.textDisabled : EditorPalette.text), label);
            draw.PopClipRect();
            if (Vector2.DistanceSquared(point, ImGuiApi.GetMousePos()) <= 64)
                Widget.DrawTooltip(port.type.id + (missing
                    ? " · missing port; reconnect explicitly"
                    : optional ? " · optional input" : port.direction == GraphPortDirection.Input ? " · required input" : ""));
        }
        if (!draft.readOnly)
        {
            bool resizeHovered = ContainsResizeHandle(rect, ImGuiApi.GetMousePos());
            uint gripColor = Color(
                0.62f,
                0.63f,
                0.69f,
                resizeHovered || draft.resizingNode == node.id
                    ? EditorPalette.opacityEmphasized
                    : EditorPalette.opacityMedium);
            const float inset = 4f;
            draw.AddLine(rect.max - new Vector2(C_RESIZE_HANDLE, inset) * zoom, rect.max - new Vector2(inset, C_RESIZE_HANDLE) * zoom, gripColor);
            draw.AddLine(rect.max - new Vector2(C_RESIZE_HANDLE * 0.62f, inset) * zoom, rect.max - new Vector2(inset, C_RESIZE_HANDLE * 0.62f) * zoom, gripColor);
        }
        if (draft.ports[node.id].Length == 0 && node.definitionId == ShaderGraphNodes.inputDefinitionId)
            draw.AddText(
                ImGuiApi.GetFont(),
                ImGuiApi.GetFontSize() * zoom,
                rect.min + new Vector2(12, C_HEADER + 8) * zoom,
                ImGuiApi.GetColorU32(ImGuiCol.TextDisabled),
                "No caller inputs");
        if (draft.nodeErrors.TryGetValue(node.id, out string? error))
        {
            draw.AddCircleFilled(new(rect.max.X - 14 * zoom, rect.min.Y + 16 * zoom), 4 * zoom, Color(1, 0.4f, 0.35f));
            if (Contains(rect, ImGuiApi.GetMousePos())) Widget.DrawTooltip(error);
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
    private unsafe void Groups(ImDrawListPtr draw)
    {
        foreach (ShaderCanvasGroup group in draft.groups)
        {
            if (GroupRect(group) is not { } bounds) continue;
            Vector2 min = bounds.min, max = bounds.max;
            bool selected = draft.selectedGroupId == group.id;
            Vector4 headerColor = GroupHeaderColor(group);
            Vector4 bodyColor = EditorPalette.WithOpacity(headerColor, EditorPalette.opacitySoft);
            Vector4 borderColor = selected
                ? new(0.65f, 0.47f, 0.88f, EditorPalette.opacityOpaque)
                : new(
                    MathF.Min(1f, headerColor.X * 1.22f),
                    MathF.Min(1f, headerColor.Y * 1.22f),
                    MathF.Min(1f, headerColor.Z * 1.22f),
                    EditorPalette.opacityEmphasized);
            float headerHeight = C_HEADER * Canvas.zoom;
            draw.AddRectFilled(min, max, ImGuiApi.ColorConvertFloat4ToU32(bodyColor), 8 * Canvas.zoom);
            draw.AddRectFilled(min, new(max.X, min.Y + headerHeight), ImGuiApi.ColorConvertFloat4ToU32(headerColor),
                8 * Canvas.zoom, ImDrawFlags.RoundCornersTop);
            draw.AddLine(new(min.X, min.Y + headerHeight), new(max.X, min.Y + headerHeight),
                ImGuiApi.ColorConvertFloat4ToU32(borderColor));
            draw.AddRect(min, max, ImGuiApi.ColorConvertFloat4ToU32(borderColor), 8 * Canvas.zoom,
                ImDrawFlags.None, selected ? 2f : 1f);
            draw.AddText(ImGuiApi.GetFont(), ImGuiApi.GetFontSize() * Canvas.zoom, min + new Vector2(12, 7) * Canvas.zoom,
                ImGuiApi.GetColorU32(ImGuiCol.Text), group.title);
        }
    }
    private ShaderCanvasGroup? HitGroupHeader(Vector2 point)
    {
        foreach (ShaderCanvasGroup group in draft.groups.Reverse())
            if (GroupRect(group) is { } bounds && point.X >= bounds.min.X && point.X <= bounds.max.X
                && point.Y >= bounds.min.Y && point.Y <= bounds.min.Y + 32f * Canvas.zoom)
                return group;
        return null;
    }
    private (Vector2 min, Vector2 max)? GroupRect(ShaderCanvasGroup group)
    {
        Vector2 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (string id in group.nodes)
            if (Controller.document.FindNode(new(id)) is GraphNodeRecord node)
            { var rect = Rect(node); min = Vector2.Min(min, rect.min); max = Vector2.Max(max, rect.max); }
        if (min.X == float.MaxValue) return null;
        return (min - new Vector2(20, 40) * Canvas.zoom, max + new Vector2(20) * Canvas.zoom);
    }
    private Dictionary<PortHandle, Vector2> PortPositions()
    {
        var points = new Dictionary<PortHandle, Vector2>();
        foreach (GraphNodeRecord node in Controller.document.nodes)
        {
            var rect = Rect(node);
            int left = 0, right = 0;
            foreach (ShaderNodePort port in draft.ports[node.id])
            {
                int row = port.direction == GraphPortDirection.Input ? left++ : right++;
                var handle = new PortHandle(new(node.id, new(port.id)), port.direction);
                points[handle] = new(port.direction == GraphPortDirection.Input ? rect.min.X : rect.max.X,
                    rect.min.Y + (C_HEADER + C_ROW * (row + 0.5f)) * Canvas.zoom);
            }
        }
        return points;
    }
    private void Edges(ImDrawListPtr draw, Dictionary<PortHandle, Vector2> points)
    {
        foreach (GraphEdgeRecord edge in Controller.document.edges)
            if (points.TryGetValue(new(edge.output, GraphPortDirection.Output), out Vector2 a)
                && points.TryGetValue(new(edge.input, GraphPortDirection.Input), out Vector2 b))
                Curve(a, b, edge.id == draft.selectedEdge);
        if (Canvas.pendingConnection is GraphEndpoint pending
            && Canvas.pendingConnectionDirection is GraphPortDirection direction
            && points.TryGetValue(new(pending, direction), out Vector2 start))
        {
            if (direction == GraphPortDirection.Output) Curve(start, ImGuiApi.GetMousePos());
            else Curve(ImGuiApi.GetMousePos(), start);
        }
        void Curve(Vector2 a, Vector2 b, bool selected = false)
        {
            float tangent = MathF.Max(36, MathF.Abs(b.X - a.X) * 0.45f);
            draw.AddBezierCubic(a, a + new Vector2(tangent, 0), b - new Vector2(tangent, 0), b,
                selected ? Color(0.87f, 0.72f, 1f) : Color(0.59f, 0.46f, 0.82f), selected ? 3 : 2);
        }
    }
    private GraphEdgeId? HitEdge(Vector2 mouse, Dictionary<PortHandle, Vector2> points)
    {
        foreach (GraphEdgeRecord edge in Controller.document.edges)
        {
            if (!points.TryGetValue(new(edge.output, GraphPortDirection.Output), out Vector2 a)
                || !points.TryGetValue(new(edge.input, GraphPortDirection.Input), out Vector2 b)) continue;
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
        return (min, min + new Vector2(NodeWidth(node), C_HEADER + Rows(node) * C_ROW + ControlHeight(node) + 20) * Canvas.zoom);
    }
    private static float ControlHeight(GraphNodeRecord node) => 0f;
    private int Rows(GraphNodeRecord node)
    {
        int rows = Math.Max(
            draft.ports[node.id].Count(static port => port.direction == GraphPortDirection.Input),
            draft.ports[node.id].Count(static port => port.direction == GraphPortDirection.Output));
        return rows == 0 && node.definitionId is ShaderGraphNodes.inputDefinitionId or ShaderGraphNodes.outputDefinitionId ? 1 : rows;
    }
    private static bool UsesSplitPortLayout(IEnumerable<ShaderNodePort> ports)
        => ports.Any(static port => port.direction == GraphPortDirection.Input)
           && ports.Any(static port => port.direction == GraphPortDirection.Output);
    private GraphNodeRecord? HitNode(Vector2 point) => Controller.document.nodes.Reverse().FirstOrDefault(node => Contains(Rect(node), point));
    private PortHandle? HitPort(Vector2 point, Dictionary<PortHandle, Vector2> ports)
    {
        foreach ((PortHandle handle, Vector2 position) in ports)
            if (Vector2.DistanceSquared(point, position) <= MathF.Pow(MathF.Max(8, 7 * Canvas.zoom), 2)) return handle;
        foreach ((PortHandle handle, Vector2 position) in ports)
        {
            GraphNodeRecord node = Controller.document.FindNode(handle.endpoint.nodeId)!;
            (Vector2 min, Vector2 max) rect = Rect(node);
            bool splitPorts = UsesSplitPortLayout(draft.ports[node.id]);
            float divider = (rect.min.X + rect.max.X) * 0.5f;
            bool input = handle.direction == GraphPortDirection.Input;
            float minimumX = splitPorts && !input ? divider : rect.min.X;
            float maximumX = splitPorts && input ? divider : rect.max.X;
            float halfRow = C_ROW * Canvas.zoom * 0.5f;
            if (point.X >= minimumX && point.X <= maximumX && point.Y >= position.Y - halfRow && point.Y <= position.Y + halfRow)
                return handle;
        }
        return null;
    }
    private GraphNodeRecord? HitResizeHandle(Vector2 point)
    {
        foreach (GraphNodeRecord node in Controller.document.nodes.Reverse())
        {
            (Vector2 min, Vector2 max) rect = Rect(node);
            if (ContainsResizeHandle(rect, point))
                return node;
        }
        return null;
    }
    private bool ContainsResizeHandle((Vector2 min, Vector2 max) rect, Vector2 point)
    {
        float extent = MathF.Max(10f, C_RESIZE_HANDLE * Canvas.zoom);
        return point.X >= rect.max.X - extent && point.X <= rect.max.X + 2f &&
               point.Y >= rect.max.Y - extent && point.Y <= rect.max.Y + 2f;
    }
    private float NodeWidth(GraphNodeRecord node, bool includePreview = true)
    {
        if (includePreview && draft.resizingNode == node.id)
            return draft.resizePreviewWidth;
        float width = Read(node, C_NODE_WIDTH, C_DEFAULT_WIDTH);
        return float.IsFinite(width) ? Math.Clamp(width, C_MINIMUM_WIDTH, C_MAXIMUM_WIDTH) : C_DEFAULT_WIDTH;
    }
    private static string FitPortLabel(string label, float availableWidth, float zoom)
    {
        if (ImGuiApi.CalcTextSize(label).X * zoom <= availableWidth)
            return label;
        const string ellipsis = "…";
        float ellipsisWidth = ImGuiApi.CalcTextSize(ellipsis).X * zoom;
        if (ellipsisWidth >= availableWidth)
            return string.Empty;
        int low = 0;
        int high = label.Length;
        while (low < high)
        {
            int length = (low + high + 1) / 2;
            if (ImGuiApi.CalcTextSize(label[..length]).X * zoom + ellipsisWidth <= availableWidth)
                low = length;
            else
                high = length - 1;
        }
        return label[..low] + ellipsis;
    }
    private ShaderNodePort Port(PortHandle handle)
        => draft.ports[handle.endpoint.nodeId].Single(port =>
            port.id == handle.endpoint.portId.value && port.direction == handle.direction);
    private ShaderNodePort Port(GraphEndpoint endpoint) => draft.ports[endpoint.nodeId].Single(port => port.id == endpoint.portId.value);
    private GraphPosition ToGraph(Vector2 point) => new((point.X - m_origin.X - Canvas.pan.x) / Canvas.zoom, (point.Y - m_origin.Y - Canvas.pan.y) / Canvas.zoom);
    private static bool Contains((Vector2 min, Vector2 max) rect, Vector2 point) => point.X >= rect.min.X && point.Y >= rect.min.Y && point.X <= rect.max.X && point.Y <= rect.max.Y;
    private static uint Color(float r, float g, float b, float a = EditorPalette.opacityOpaque)
        => ImGuiApi.ColorConvertFloat4ToU32(new(r, g, b, a));

    private Vector4 NodeHeaderColor(GraphNodeRecord node)
    {
        string key = node.definitionId + "/" + Title(node);
        return node.definitionId switch
        {
            ShaderGraphDocument.outputDefinitionId => StableHeaderColor(key, 258f, 34f),
            ShaderGraphNodes.outputDefinitionId => StableHeaderColor(key, 258f, 34f),
            "inno.shader.stage-input" or "inno.shader.constant" or ShaderGraphNodes.inputDefinitionId
                => StableHeaderColor(key, 22f, 24f),
            "inno.shader.sample" => StableHeaderColor(key, 158f, 30f),
            "inno.shader.source" => StableHeaderColor(key, 198f, 42f),
            "inno.shader.binary" => StableHeaderColor(key, 205f, 45f),
            "inno.shader.construct" or "inno.shader.extract" or "inno.shader.select" or "inno.shader.reroute"
                => StableHeaderColor(key, 178f, 58f),
            "inno.shader.storage-load" or "inno.shader.storage-store" or "inno.shader.storage-atomic-add"
                => StableHeaderColor(key, 105f, 42f),
            "inno.shader.discard" => StableHeaderColor(key, 350f, 22f),
            _ => StableHeaderColor(key, 0f, 360f)
        };
    }

    private Vector4 GroupHeaderColor(ShaderCanvasGroup group)
    {
        Vector4 total = Vector4.Zero;
        int count = 0;
        foreach (string id in group.nodes)
        {
            if (Controller.document.FindNode(new(id)) is not GraphNodeRecord node) continue;
            total += NodeHeaderColor(node);
            count++;
        }
        if (count == 0) return EditorPalette.shaderNodeHeader;
        Vector4 average = total / count;
        return EditorPalette.WithOpacity(average, EditorPalette.opacityNearOpaque);
    }

    private static Vector4 StableHeaderColor(string key, float startHue, float hueRange)
    {
        uint hash = 2166136261;
        foreach (char value in key)
        {
            hash ^= value;
            hash *= 16777619;
        }
        float hue = (startHue + hueRange * (hash & 0xffff) / 65535f) % 360f;
        float saturation = 0.52f + 0.12f * ((hash >> 16) & 0xff) / 255f;
        float brightness = 0.34f + 0.08f * ((hash >> 24) & 0xff) / 255f;
        float chroma = brightness * saturation;
        float sector = hue / 60f;
        float secondary = chroma * (1f - MathF.Abs(sector % 2f - 1f));
        (float red, float green, float blue) = sector switch
        {
            < 1f => (chroma, secondary, 0f),
            < 2f => (secondary, chroma, 0f),
            < 3f => (0f, chroma, secondary),
            < 4f => (0f, secondary, chroma),
            < 5f => (secondary, 0f, chroma),
            _ => (chroma, 0f, secondary)
        };
        float match = brightness - chroma;
        return new(red + match, green + match, blue + match, EditorPalette.opacityOpaque);
    }

    private static bool CenteredAddButton(string label)
        => Widget.CenteredButton(label, Widget.style.inspectorAddButtonTopPadding);
    private T Read<T>(GraphNodeRecord node, string key, T value) => ShaderGraphDocument.Read(node, key, value, owner.serialization, owner.context);
    private void Set<T>(GraphNodeRecord node, string key, T value, bool continuous = false)
        => SetEncoded(node, key, ShaderGraphDocument.Encode(value, owner.serialization, owner.context), continuous);
    private void SetEncoded(GraphNodeRecord node, string key, GraphSerializedValue value, bool continuous)
    {
        if (ImGuiApi.IsItemActivated()) draft.valueGesture = Guid.NewGuid().ToString("N");
        if (m_inspectionNodes is { Length: > 1 } selected)
        {
            GraphDocument candidate = Controller.document.Clone();
            foreach (GraphNodeRecord target in selected)
            {
                if (key == "settings" && target.definitionId == "inno.shader.stage-input")
                    candidate = ShaderGraphBindings.ChangeInput(candidate, target.id,
                        ShaderGraphDocument.Decode<ShaderGraphInputSettings>(value, owner.serialization, owner.context), owner.serialization, owner.context);
                else candidate.FindNode(target.id)!.SetValue(key, value.Clone());
            }
            Controller.ReplaceDocument(candidate, "Edit Shader Nodes", continuous ? draft.valueGesture : null);
        }
        else if (key == "settings" && node.definitionId == "inno.shader.stage-input")
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
        float maxX = selection.Max(node => node.position.x + NodeWidth(node));
        float maxY = selection.Max(node => node.position.y + C_HEADER + Rows(node) * C_ROW + ControlHeight(node) + 20);
        float scale = Math.Clamp(MathF.Min(MathF.Max(1, m_size.X - 80) / MathF.Max(1, maxX - minX), MathF.Max(1, m_size.Y - 80) / MathF.Max(1, maxY - minY)), 0.1f, 1f);
        Canvas.SetViewport(new(m_size.X / 2 - (minX + maxX) / 2 * scale, m_size.Y / 2 - (minY + maxY) / 2 * scale), scale);
    }
    internal string Title(GraphNodeRecord node)
    {
        if (owner.drawers?.GetDisplayName(node.definitionId) is { } displayName) return displayName;
        if (node.definitionId == ShaderGraphDocument.outputDefinitionId)
            return Read(node, "settings", new ShaderGraphStageSettings()).stage + " Output";
        if (node.definitionId == "inno.shader.binary")
            return Widget.NicifyName(Read(node, "operation", "add").Replace('-', ' '));
        if (node.definitionId == "inno.shader.stage-input")
        {
            ShaderGraphInputSettings input = Read(node, "settings", new ShaderGraphInputSettings());
            return input.id.Length != 0 ? Widget.NicifyName(input.id) : "Stage Input";
        }
        if (node.definitionId == "inno.shader.source")
        {
            string function = Read(node, "function", "");
            return function.Length == 0 ? "Source Function" : function;
        }
        if (node.definitionId == ShaderGraphNodes.callDefinitionId)
            return Read(node, ShaderGraphNodes.interfaceKey, new ShaderGraphNodeInterface()).displayName;
        if (node.definitionId == ShaderGraphNodes.inputDefinitionId)
            return ShaderGraphNodes.ReadSettings(Controller.document, owner.serialization, owner.context).kind == ShaderGraphNodeKind.DomainOutput
                ? "Domain Inputs"
                : "Function Inputs";
        if (node.definitionId == ShaderGraphNodes.outputDefinitionId) return "Function Outputs";
        return Widget.NicifyName(node.definitionId.Replace("inno.shader.", "", StringComparison.Ordinal).Replace('-', ' '));
    }
}
