using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Inno.Assets;
using Inno.Assets.Core;
using Inno.Core.Graphs;
using Inno.Editor.Core;
using Inno.Editor.Graph;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Editor.Interactions;
using Inno.Native.ImGui;
using Inno.Rendering;
using Inno.Rendering.ShaderGraph;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.ShaderGraph;

/// <summary>Edits neutral raster and compute shader graphs through the shared Graph and Shader IR contracts.</summary>
[EditorPanel("rendering.shader-graph", "Shader Graph", order: 230, menuPath: "Authoring")]
internal sealed class ShaderGraphPanel : EditorPanel
{
    private const string C_DEFAULT_PATH = "NewShader.ishadergraph";
    private const float C_NODE_WIDTH = 184f;
    private const float C_NODE_HEADER_HEIGHT = 30f;
    private const float C_PORT_ROW_HEIGHT = 22f;
    private const float C_PORT_RADIUS = 6f;

    private readonly GraphEditorModule m_graphs;
    private readonly EditorInteractions m_interactions;
    private readonly GraphCanvasState m_canvas = new();
    private readonly ShaderNodeRegistry m_nodes;
    private GraphDocumentController? m_controller;
    private ShaderGraphCompileResult? m_compileResult;
    private ShaderGraphAsset? m_asset;
    private string m_documentPath = C_DEFAULT_PATH;
    private ulong m_lastCompiledRevision = ulong.MaxValue;
    private ulong m_lastNodeGeneration = ulong.MaxValue;
    private bool m_draggingSelection;

    /// <inheritdoc />
    public override bool useWindowPadding => false;

    /// <summary>Creates the Shader Graph panel around shared Graph and History services.</summary>
    internal ShaderGraphPanel(
        GraphEditorModule graphs,
        EditorInteractions interactions,
        ShaderNodeRegistry nodes)
    {
        m_graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        m_nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
    }

    /// <inheritdoc />
    protected override void OnDraw(EditorContext context)
    {
        EnsureDocument(context);
        DrawToolbar(context);
        CompileIfChanged();
        DrawCompileStatus();
        DrawCanvas();
    }

    /// <inheritdoc />
    protected override void OnDetach(EditorContext context)
    {
        _ = context;
        if (m_controller is not null)
        {
            _ = m_graphs.CloseDocument(m_controller.documentId);
            m_controller = null;
        }
    }

    /// <inheritdoc />
    protected override void Capture(EditorState state)
    {
        state.Set("document", m_documentPath);
        state.Set("panX", m_canvas.pan.x);
        state.Set("panY", m_canvas.pan.y);
        state.Set("zoom", m_canvas.zoom);
    }

    /// <inheritdoc />
    protected override void Restore(EditorState state)
    {
        m_documentPath = NormalizeDocumentPath(state.Get("document", C_DEFAULT_PATH));
        m_canvas.SetViewport(
            new GraphPosition(state.Get("panX", 48f), state.Get("panY", 48f)),
            state.Get("zoom", 1f));
    }

    internal static string NormalizeDocumentPath(string? path)
        => string.IsNullOrWhiteSpace(path) ? C_DEFAULT_PATH : path;

    private void EnsureDocument(EditorContext context)
    {
        if (m_controller is not null)
        {
            return;
        }

        m_documentPath = NormalizeDocumentPath(m_documentPath);
        GraphDocument document;
        if (AssetManager.TryLoad(AssetPath.Parse(m_documentPath), out ShaderGraphAsset? asset)
            && asset?.document is not null)
        {
            m_asset = asset;
            document = asset.document.Clone();
        }
        else
        {
            m_asset = new ShaderGraphAsset();
            document = new GraphDocument();
        }

        m_controller = m_graphs.OpenDocument(m_documentPath, document, m_interactions.history);
    }

    private void DrawToolbar(EditorContext context)
    {
        if (NativeImGui.Button("Save"))
        {
            Save(context);
        }

        NativeImGui.SameLine();
        DrawNodePicker();

        NativeImGui.SameLine();
        if (NativeImGui.Button("Delete") && m_canvas.selectedNodes.Count != 0)
        {
            m_controller!.RemoveNodes(m_canvas.selectedNodes);
            m_canvas.ClearSelection();
        }

        NativeImGui.SameLine();
        NativeImGui.TextUnformatted(
            $"{m_documentPath}{(m_controller!.isDirty ? " *" : string.Empty)}  " +
            $"Zoom {m_canvas.zoom:F2}");
    }

    private void DrawNodePicker()
    {
        NativeImGui.SameLine();
        if (!EditorWidget.BeginBoundedCombo("##shader_graph_add_node", "Add Node..."))
            return;
        try
        {
            foreach (ShaderNodeDefinition definition in m_nodes.definitions
                         .OrderBy(static value => value.category, StringComparer.Ordinal)
                         .ThenBy(static value => value.displayName, StringComparer.Ordinal)
                         .ThenBy(static value => value.id, StringComparer.Ordinal))
            {
                string label = string.IsNullOrWhiteSpace(definition.category)
                    ? definition.displayName
                    : $"{definition.category}/{definition.displayName}";
                if (NativeImGui.Selectable(label))
                    AddNode(definition.id);
            }
        }
        finally
        {
            NativeImGui.EndCombo();
        }
    }

    private void DrawCompileStatus()
    {
        if (m_compileResult is null)
        {
            return;
        }

        if (m_compileResult.succeeded)
        {
            NativeImGui.TextUnformatted(
                $"IR ready: {m_compileResult.module!.passes.Count} pass(es), " +
                $"{m_controller!.document.nodes.Count} node(s)");
            return;
        }

        ShaderDiagnostic? diagnostic = m_compileResult.diagnostics.FirstOrDefault(
            static value => value.severity == ShaderDiagnosticSeverity.Error);
        NativeImGui.TextUnformatted(diagnostic is null
            ? "Graph compilation failed."
            : $"{diagnostic.code}: {diagnostic.message}");
    }

    private void DrawCanvas()
    {
        Vector2 origin = NativeImGui.GetCursorScreenPos();
        Vector2 size = NativeImGui.GetContentRegionAvail();
        size.X = MathF.Max(1f, size.X);
        size.Y = MathF.Max(1f, size.Y);
        _ = NativeImGui.InvisibleButton(
            "##ShaderGraphCanvas",
            size,
            ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonMiddle);
        bool hovered = NativeImGui.IsItemHovered();
        Vector2 mouse = NativeImGui.GetMousePos();
        ImGuiIOPtr io = NativeImGui.GetIO();
        HandleCanvasNavigation(hovered, origin, mouse, io);

        ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
        Vector2 canvasMax = origin + size;
        drawList.PushClipRect(origin, canvasMax, true);
        try
        {
            drawList.AddRectFilled(origin, canvasMax, Pack(0.055f, 0.06f, 0.075f, 1f));
            DrawGrid(drawList, origin, canvasMax);
            Dictionary<GraphEndpoint, Vector2> ports = BuildPortPositions(origin);
            DrawEdges(drawList, ports);
            DrawNodes(drawList, origin, ports);
            HandleCanvasSelection(hovered, origin, mouse, ports, io.MouseDelta);
        }
        finally
        {
            drawList.PopClipRect();
        }
    }

    private void HandleCanvasNavigation(bool hovered, Vector2 origin, Vector2 mouse, ImGuiIOPtr io)
    {
        if (hovered && io.MouseWheel != 0f)
        {
            m_canvas.ZoomAt(
                io.MouseWheel > 0f ? 1.12f : 1f / 1.12f,
                mouse.X - origin.X,
                mouse.Y - origin.Y);
        }

        if (hovered && NativeImGui.IsMouseDown(ImGuiMouseButton.Middle))
        {
            m_canvas.PanBy(io.MouseDelta.X, io.MouseDelta.Y);
        }
    }

    private void HandleCanvasSelection(
        bool hovered,
        Vector2 origin,
        Vector2 mouse,
        IReadOnlyDictionary<GraphEndpoint, Vector2> ports,
        Vector2 mouseDelta)
    {
        if (hovered && NativeImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            GraphEndpoint? hitPort = HitPort(mouse, ports);
            if (hitPort is GraphEndpoint endpoint)
            {
                GraphPortDefinition definition = RequirePort(endpoint);
                if (definition.direction == GraphPortDirection.Output)
                {
                    m_canvas.BeginConnection(endpoint);
                }
                else if (m_canvas.pendingConnection is GraphEndpoint output)
                {
                    _ = m_controller!.Connect(output, endpoint);
                    m_canvas.CancelConnection();
                }
                return;
            }

            GraphNodeRecord? hitNode = m_controller!.document.nodes
                .Reverse()
                .FirstOrDefault(node => Contains(NodeRect(node, origin), mouse));
            if (hitNode is null)
            {
                m_canvas.ClearSelection();
                m_canvas.CancelConnection();
                m_draggingSelection = false;
            }
            else
            {
                m_canvas.SelectNodes([hitNode.id]);
                m_draggingSelection = true;
            }
        }

        if (m_draggingSelection && NativeImGui.IsMouseDown(ImGuiMouseButton.Left)
            && (mouseDelta.X != 0f || mouseDelta.Y != 0f))
        {
            Dictionary<GraphNodeId, GraphPosition> positions = m_canvas.selectedNodes.ToDictionary(
                static id => id,
                id =>
                {
                    GraphPosition current = m_controller!.document.FindNode(id)!.position;
                    return new GraphPosition(
                        current.x + mouseDelta.X / m_canvas.zoom,
                        current.y + mouseDelta.Y / m_canvas.zoom);
                });
            m_controller!.MoveNodes(positions);
        }

        if (NativeImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            m_draggingSelection = false;
        }
    }

    private void DrawGrid(ImDrawListPtr drawList, Vector2 min, Vector2 max)
    {
        float spacing = 32f * m_canvas.zoom;
        float offsetX = PositiveModulo(m_canvas.pan.x, spacing);
        float offsetY = PositiveModulo(m_canvas.pan.y, spacing);
        uint color = Pack(0.12f, 0.13f, 0.16f, 1f);
        for (float x = min.X + offsetX; x < max.X; x += spacing)
        {
            drawList.AddLine(new Vector2(x, min.Y), new Vector2(x, max.Y), color);
        }

        for (float y = min.Y + offsetY; y < max.Y; y += spacing)
        {
            drawList.AddLine(new Vector2(min.X, y), new Vector2(max.X, y), color);
        }
    }

    private Dictionary<GraphEndpoint, Vector2> BuildPortPositions(Vector2 origin)
    {
        Dictionary<GraphEndpoint, Vector2> positions = [];
        foreach (GraphNodeRecord node in m_controller!.document.nodes)
        {
            if (!m_nodes.TryResolveShader(node.definitionId, out ShaderNodeDefinition? definition)
                || definition is null)
            {
                continue;
            }

            IReadOnlyList<GraphPortDefinition> ports = definition.GetPorts(node);
            Rect rect = NodeRect(node, origin);
            int inputIndex = 0;
            int outputIndex = 0;
            foreach (GraphPortDefinition port in ports)
            {
                int row = port.direction == GraphPortDirection.Input ? inputIndex++ : outputIndex++;
                positions.Add(
                    new GraphEndpoint(node.id, port.id),
                    new Vector2(
                        port.direction == GraphPortDirection.Input ? rect.min.X : rect.max.X,
                        rect.min.Y + (C_NODE_HEADER_HEIGHT + C_PORT_ROW_HEIGHT * (row + 0.5f)) * m_canvas.zoom));
            }
        }

        return positions;
    }

    private void DrawEdges(ImDrawListPtr drawList, IReadOnlyDictionary<GraphEndpoint, Vector2> ports)
    {
        uint color = Pack(0.35f, 0.72f, 1f, 1f);
        foreach (GraphEdgeRecord edge in m_controller!.document.edges)
        {
            if (!ports.TryGetValue(edge.output, out Vector2 output)
                || !ports.TryGetValue(edge.input, out Vector2 input))
            {
                continue;
            }

            float tangent = MathF.Max(40f, MathF.Abs(input.X - output.X) * 0.45f);
            drawList.AddBezierCubic(
                output,
                output + new Vector2(tangent, 0f),
                input - new Vector2(tangent, 0f),
                input,
                color,
                2.5f);
        }

        if (m_canvas.pendingConnection is GraphEndpoint pending
            && ports.TryGetValue(pending, out Vector2 start))
        {
            Vector2 end = NativeImGui.GetMousePos();
            float tangent = MathF.Max(40f, MathF.Abs(end.X - start.X) * 0.45f);
            drawList.AddBezierCubic(
                start,
                start + new Vector2(tangent, 0f),
                end - new Vector2(tangent, 0f),
                end,
                color,
                2f);
        }
    }

    private void DrawNodes(
        ImDrawListPtr drawList,
        Vector2 origin,
        IReadOnlyDictionary<GraphEndpoint, Vector2> portPositions)
    {
        foreach (GraphNodeRecord node in m_controller!.document.nodes)
        {
            bool resolved = m_nodes.TryResolveShader(node.definitionId, out ShaderNodeDefinition? definition)
                && definition is not null;
            Rect rect = NodeRect(node, origin);
            bool selected = m_canvas.selectedNodes.Contains(node.id);
            uint body = resolved
                ? Pack(0.11f, 0.12f, 0.15f, 0.98f)
                : Pack(0.28f, 0.08f, 0.09f, 0.98f);
            drawList.AddRectFilled(rect.min, rect.max, body, 7f * m_canvas.zoom);
            drawList.AddRect(
                rect.min,
                rect.max,
                selected ? Pack(0.25f, 0.72f, 1f, 1f) : Pack(0.22f, 0.24f, 0.3f, 1f),
                7f * m_canvas.zoom,
                ImDrawFlags.None,
                selected ? 2.5f : 1f);
            drawList.AddRectFilled(
                rect.min,
                new Vector2(rect.max.X, rect.min.Y + C_NODE_HEADER_HEIGHT * m_canvas.zoom),
                resolved ? Pack(0.17f, 0.25f, 0.36f, 1f) : Pack(0.5f, 0.12f, 0.13f, 1f),
                7f * m_canvas.zoom);
            drawList.AddText(
                rect.min + new Vector2(10f, 7f) * m_canvas.zoom,
                Pack(0.93f, 0.95f, 1f, 1f),
                resolved ? definition!.displayName : $"Missing: {node.definitionId}");
            if (!resolved)
            {
                continue;
            }

            int inputIndex = 0;
            int outputIndex = 0;
            foreach (GraphPortDefinition port in definition!.GetPorts(node))
            {
                GraphEndpoint endpoint = new(node.id, port.id);
                Vector2 center = portPositions[endpoint];
                drawList.AddCircleFilled(center, C_PORT_RADIUS * m_canvas.zoom, PortColor(port.valueTypeId));
                int row = port.direction == GraphPortDirection.Input ? inputIndex++ : outputIndex++;
                float y = rect.min.Y
                    + (C_NODE_HEADER_HEIGHT + C_PORT_ROW_HEIGHT * row + 4f) * m_canvas.zoom;
                if (port.direction == GraphPortDirection.Input)
                {
                    drawList.AddText(
                        new Vector2(rect.min.X + 12f * m_canvas.zoom, y),
                        Pack(0.82f, 0.84f, 0.9f, 1f),
                        port.displayName);
                }
                else
                {
                    Vector2 labelSize = NativeImGui.CalcTextSize(port.displayName);
                    drawList.AddText(
                        new Vector2(rect.max.X - (12f * m_canvas.zoom) - labelSize.X, y),
                        Pack(0.82f, 0.84f, 0.9f, 1f),
                        port.displayName);
                }
            }
        }
    }

    private void AddNode(string definitionId)
    {
        Vector2 size = NativeImGui.GetContentRegionAvail();
        GraphPosition position = new(
            (size.X * 0.5f - m_canvas.pan.x) / m_canvas.zoom,
            (size.Y * 0.5f - m_canvas.pan.y) / m_canvas.zoom);
        GraphNodeId id = m_controller!.AddNode(definitionId, position);
        m_canvas.SelectNodes([id]);
    }

    private void CompileIfChanged()
    {
        ulong nodeGeneration = m_nodes.generation;
        if (m_controller!.revision == m_lastCompiledRevision
            && nodeGeneration == m_lastNodeGeneration)
        {
            return;
        }

        m_compileResult = ShaderGraphCompiler.Compile(
            m_documentPath,
            Path.GetFileNameWithoutExtension(m_documentPath),
            m_controller.document,
            m_nodes);
        m_lastCompiledRevision = m_controller.revision;
        m_lastNodeGeneration = nodeGeneration;
    }

    private void Save(EditorContext context)
    {
        _ = context;
        m_asset ??= new ShaderGraphAsset();
        m_asset.SetDocument(m_controller!.document);
        if (!AssetManager.Save(AssetPath.Parse(m_documentPath), m_asset))
            throw new InvalidOperationException($"No Shader Graph importer can save '{m_documentPath}'.");
        m_controller.MarkSaved();
    }

    private GraphPortDefinition RequirePort(GraphEndpoint endpoint)
    {
        GraphNodeRecord node = m_controller!.document.FindNode(endpoint.nodeId)
            ?? throw new InvalidOperationException($"Graph node '{endpoint.nodeId}' is unavailable.");
        if (!m_nodes.TryResolveShader(node.definitionId, out ShaderNodeDefinition? definition)
            || definition is null)
        {
            throw new InvalidOperationException($"Graph node definition '{node.definitionId}' is unavailable.");
        }

        return definition.GetPorts(node).First(port => port.id == endpoint.portId);
    }

    private GraphEndpoint? HitPort(Vector2 mouse, IReadOnlyDictionary<GraphEndpoint, Vector2> ports)
    {
        float radius = (C_PORT_RADIUS + 5f) * m_canvas.zoom;
        float radiusSquared = radius * radius;
        foreach ((GraphEndpoint endpoint, Vector2 center) in ports)
        {
            if (Vector2.DistanceSquared(mouse, center) <= radiusSquared)
            {
                return endpoint;
            }
        }

        return null;
    }

    private Rect NodeRect(GraphNodeRecord node, Vector2 origin)
    {
        float portRows = 1f;
        if (m_nodes.TryResolveShader(node.definitionId, out ShaderNodeDefinition? definition)
            && definition is not null)
        {
            IReadOnlyList<GraphPortDefinition> ports = definition.GetPorts(node);
            portRows = MathF.Max(
                ports.Count(static port => port.direction == GraphPortDirection.Input),
                ports.Count(static port => port.direction == GraphPortDirection.Output));
        }

        Vector2 min = origin
            + new Vector2(m_canvas.pan.x, m_canvas.pan.y)
            + new Vector2(node.position.x, node.position.y) * m_canvas.zoom;
        Vector2 max = min + new Vector2(
            C_NODE_WIDTH,
            C_NODE_HEADER_HEIGHT + C_PORT_ROW_HEIGHT * MathF.Max(1f, portRows)) * m_canvas.zoom;
        return new Rect(min, max);
    }

    private static bool Contains(Rect rect, Vector2 value)
        => value.X >= rect.min.X && value.Y >= rect.min.Y
            && value.X <= rect.max.X && value.Y <= rect.max.Y;

    private static float PositiveModulo(float value, float divisor)
        => ((value % divisor) + divisor) % divisor;

    private static uint PortColor(string valueTypeId)
        => valueTypeId.Contains("texture", StringComparison.OrdinalIgnoreCase)
            ? Pack(0.92f, 0.42f, 0.8f, 1f)
            : valueTypeId.Contains("float", StringComparison.OrdinalIgnoreCase)
                ? Pack(0.38f, 0.8f, 0.5f, 1f)
                : Pack(0.9f, 0.68f, 0.3f, 1f);

    private static uint Pack(float r, float g, float b, float a)
        => NativeImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, a));

    private readonly record struct Rect(Vector2 min, Vector2 max);
}
