using Inno.Core.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Graph;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Editor.Interactions;
using Inno.Native.ImGui;
using Inno.Rendering;
using Inno.Rendering.MaterialGraph;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.MaterialGraph;

/// <summary>
/// Edits ordinary runtime materials through a reflected, shader-backed node mapping.
/// </summary>
[EditorPanel("rendering.material-graph", "Material Graph", order: 230, menuPath: "Authoring")]
internal sealed class MaterialGraphPanel : EditorPanel
{
    internal const string C_PANEL_ID = "rendering.material-graph";
    private const string C_DEFAULT_PATH = "NewMaterial.imaterial";
    private const float C_NODE_WIDTH = 184f;
    private const float C_NODE_HEADER_HEIGHT = 30f;
    private const float C_PORT_ROW_HEIGHT = 22f;
    private const float C_PORT_RADIUS = 6f;
    private const float C_BLACKBOARD_WIDTH = 286f;
    private const nuint C_PATH_CAPACITY = 512;
    private const string C_UNSAVED_POPUP = "Unsaved Material Graph##material_graph_unsaved";

    private readonly GraphEditorModule m_graphs;
    private readonly AssetPipeline m_assets;
    private readonly SerializationRegistry m_serialization;
    private readonly EditorInteractions m_interactions;
    private readonly GraphCanvasState m_canvas = new();
    private MaterialGraphNodeResolver m_nodes;
    private GraphDocumentController? m_controller;
    private MaterialAsset? m_asset;
    private MaterialGraphEvaluationResult? m_evaluation;
    private Guid m_documentId;
    private bool m_sourceAssigned;
    private string m_documentPath = C_DEFAULT_PATH;
    private ulong m_lastCompiledRevision = ulong.MaxValue;
    private bool m_draggingSelection;
    private PendingDocumentAction m_pendingAction;
    private MaterialAsset? m_pendingAsset;
    private string m_statusMessage = string.Empty;
    private bool m_statusIsError;
    private ShaderAsset? m_savedShader;
    private ShaderTechniqueId m_savedTechnique;
    private MaterialPropertyEntry[] m_savedProperties = [];
    private string? m_savedDocumentData;

    /// <summary>
    /// Gets whether use window padding is enabled for this implementation.
    /// </summary>
    public override bool useWindowPadding => false;

    /// <summary>
    /// Creates the Material Graph panel around shared Graph and History services.
    /// </summary>
    internal MaterialGraphPanel(
        GraphEditorModule graphs,
        EditorInteractions interactions,
        AssetPipeline assets,
        SerializationRegistry serialization)
    {
        m_graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        m_assets = assets ?? throw new ArgumentNullException(nameof(assets));
        m_serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
        m_nodes = new MaterialGraphNodeResolver(null, m_serialization);
    }

    /// <summary>
    /// Draws this feature using the current editor presentation context.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnDraw(EditorContext context)
    {
        OpenSelectedAssetFromInteraction();
        if (!EnsureDocument())
        {
            NativeImGui.TextUnformatted($"Missing: {m_documentPath}. Authored graph and History are retained until its identity returns.");
            return;
        }
        DrawToolbar(context);
        DrawUnsavedPopup(context);
        EvaluateIfChanged();
        Vector2 available = NativeImGui.GetContentRegionAvail();
        bool blackboardVisible = NativeImGui.BeginChild(
            "##material_graph_blackboard",
            new Vector2(MathF.Min(C_BLACKBOARD_WIDTH, MathF.Max(180f, available.X * 0.36f)), 0f),
            ImGuiChildFlags.Borders | ImGuiChildFlags.AlwaysUseWindowPadding);
        try
        {
            if (blackboardVisible)
                DrawBlackboard();
        }
        finally
        {
            NativeImGui.EndChild();
        }
        NativeImGui.SameLine(0f, 0f);
        bool canvasVisible = NativeImGui.BeginChild("##material_graph_workspace", Vector2.Zero);
        try
        {
            if (canvasVisible)
                DrawCanvas();
        }
        finally
        {
            NativeImGui.EndChild();
        }
    }

    /// <summary>
    /// Detaches this feature and releases generation-scoped state.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnDetach(EditorContext context)
    {
        _ = context;
        if (m_controller?.isDirty == true)
            RestoreAssetSnapshot();
        m_evaluation = null;
        m_asset = null;
        m_pendingAction = PendingDocumentAction.None;
        m_pendingAsset = null;
        if (m_controller is not null)
        {
            m_controller = null;
        }
    }

    /// <summary>
    /// Captures an immutable snapshot of the current observable state.
    /// </summary>
    /// <param name="state">
    /// The lifecycle or domain state applied by this operation.
    /// </param>
    protected override void Capture(EditorState state)
    {
        state.Set("document", m_documentPath);
        state.Set("documentId", m_documentId.ToString("D"));
        state.Set("sourceAssigned", m_sourceAssigned);
        state.Set("panX", m_canvas.pan.x);
        state.Set("panY", m_canvas.pan.y);
        state.Set("zoom", m_canvas.zoom);
    }

    /// <summary>
    /// Restores the supplied snapshot while preserving current invariants.
    /// </summary>
    /// <param name="state">
    /// The lifecycle or domain state applied by this operation.
    /// </param>
    protected override void Restore(EditorState state)
    {
        string restoredPath = NormalizeDocumentPath(state.Get("document", C_DEFAULT_PATH));
        if (string.Equals(Path.GetExtension(restoredPath), ".imaterialgraph", StringComparison.OrdinalIgnoreCase))
        {
            // Panel state is not an asset migration surface. Discard the removed standalone
            // graph identity so the new one-material/one-graph workflow starts cleanly.
            m_documentPath = C_DEFAULT_PATH;
            m_documentId = Guid.Empty;
            m_sourceAssigned = false;
        }
        else
        {
            m_documentPath = restoredPath;
            _ = Guid.TryParse(state.Get("documentId", string.Empty), out m_documentId);
            m_sourceAssigned = state.Get("sourceAssigned", false);
        }
        m_canvas.SetViewport(
            new GraphPosition(state.Get("panX", 48f), state.Get("panY", 48f)),
            state.Get("zoom", 1f));
    }

    internal static string NormalizeDocumentPath(string? path)
        => string.IsNullOrWhiteSpace(path) ? C_DEFAULT_PATH : path;

    private void OpenSelectedAssetFromInteraction()
    {
        if (!TryGetSelectedMaterial(out MaterialAsset? selected)
            || selected is null
            || selected.identity.persistentId == m_documentId)
        {
            return;
        }
        RequestOpen(selected);
    }

    private bool TryGetSelectedMaterial(out MaterialAsset? material)
    {
        if (m_interactions.selection.selectedTarget is MaterialAsset selected)
        {
            material = selected;
            return true;
        }
        if (m_interactions.selection.selectedTarget is AssetFileEntry { isDirectory: false } entry)
            return m_assets.TryLoad(entry.assetPath, out material) && material is not null;
        material = null;
        return false;
    }

    private bool EnsureDocument()
    {
        m_documentPath = NormalizeDocumentPath(m_documentPath);
        MaterialAsset? asset = null;
        bool found = m_sourceAssigned
            ? m_assets.TryLoad(m_documentId, out asset)
            : m_assets.TryLoad(AssetPath.Parse(m_documentPath), out asset);
        if (found && asset is not null)
        {
            m_documentId = asset.identity.persistentId;
            m_documentPath = asset.assetPath.ToString();
            m_sourceAssigned = true;
            m_graphs.SetAvailability(m_documentId, true);
            m_asset = asset;
            m_nodes = new MaterialGraphNodeResolver(asset.shader, m_serialization);
        }
        else if (m_sourceAssigned || m_documentPath != C_DEFAULT_PATH)
        {
            m_graphs.SetAvailability(m_documentId, false);
            return false;
        }
        if (m_documentId == Guid.Empty)
            m_documentId = Guid.NewGuid();
        m_asset ??= CreateNewAsset();
        if (m_controller is not null && m_controller.isAvailable) return true;
        if (!m_graphs.TryOpenDocument(m_documentId, m_interactions.history, out m_controller))
        {
            GraphDocument document = asset is not null
                ? MaterialGraphDocumentStore.ReadOrCreate(asset, m_serialization)
                : MaterialGraphDocumentFactory.Create(m_asset.shader, m_serialization);
            m_controller = m_graphs.OpenDocument(m_documentId, document, m_interactions.history);
        }
        CaptureAssetSnapshot(m_asset);
        return true;
    }

    private void DrawToolbar(EditorContext context)
    {
        NativeImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.38f, 0.28f, 0.55f, 1f));
        NativeImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.48f, 0.36f, 0.68f, 1f));
        if (NativeImGui.Button("Save Material"))
            _ = TrySave(context);
        NativeImGui.PopStyleColor(2);

        NativeImGui.SameLine();
        if (NativeImGui.Button("New Material"))
            RequestNew();

        NativeImGui.SameLine();
        NativeImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.72f, 0.74f, 0.8f, 1f));
        NativeImGui.TextUnformatted(Path.GetFileNameWithoutExtension(m_documentPath));
        NativeImGui.PopStyleColor();
        NativeImGui.SameLine();
        NativeImGui.PushStyleColor(
            ImGuiCol.Text,
            m_controller!.isDirty
                ? new Vector4(0.84f, 0.68f, 0.34f, 1f)
                : new Vector4(0.42f, 0.78f, 0.56f, 1f));
        NativeImGui.TextUnformatted(m_controller.isDirty ? "• Unsaved" : "• Saved");
        NativeImGui.PopStyleColor();

        if (!m_sourceAssigned)
        {
            NativeImGui.SameLine();
            NativeImGui.SetNextItemWidth(MathF.Max(180f, NativeImGui.GetContentRegionAvail().X));
            string path = m_documentPath;
            if (NativeImGui.InputText("##material_path", ref path, C_PATH_CAPACITY))
                m_documentPath = NormalizeDocumentPath(path);
        }
        if (m_statusIsError && !string.IsNullOrWhiteSpace(m_statusMessage))
        {
            NativeImGui.PushStyleColor(
                ImGuiCol.Text,
                new Vector4(0.96f, 0.38f, 0.38f, 1f));
            NativeImGui.TextWrapped(m_statusMessage);
            NativeImGui.PopStyleColor();
        }
    }

    private void DrawUnsavedPopup(EditorContext context)
    {
        if (!NativeImGui.BeginPopupModal(C_UNSAVED_POPUP))
            return;
        try
        {
            NativeImGui.TextWrapped(
                $"'{m_documentPath}' contains unsaved material mapping changes.");
            NativeImGui.Dummy(new Vector2(0f, 6f));
            NativeImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.38f, 0.28f, 0.55f, 1f));
            NativeImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.48f, 0.36f, 0.68f, 1f));
            bool save = NativeImGui.Button("Save and Continue");
            NativeImGui.PopStyleColor(2);
            NativeImGui.SameLine();
            bool discard = NativeImGui.Button("Discard");
            NativeImGui.SameLine();
            bool cancel = NativeImGui.Button("Cancel");
            if (save && TrySave(context))
            {
                ExecutePendingAction();
                NativeImGui.CloseCurrentPopup();
            }
            else if (discard)
            {
                RestoreAssetSnapshot();
                ExecutePendingAction();
                NativeImGui.CloseCurrentPopup();
            }
            else if (cancel)
            {
                ClearPendingAction();
                if (m_asset is not null)
                    m_interactions.SetSelection(m_asset);
                NativeImGui.CloseCurrentPopup();
            }
        }
        finally
        {
            NativeImGui.EndPopup();
        }
    }

    private void DrawBlackboard()
    {
        NativeImGui.Dummy(new Vector2(0f, 6f));
        NativeImGui.TextUnformatted(Path.GetFileNameWithoutExtension(m_documentPath));
        NativeImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.52f, 0.58f, 1f));
        NativeImGui.TextWrapped("One asset · one graph · one runtime material");
        NativeImGui.PopStyleColor();
        NativeImGui.Separator();
        DrawShaderPicker();
        NativeImGui.Dummy(new Vector2(0f, 8f));
        NativeImGui.SeparatorText("Properties");
        DrawSelectedNodeEditor();
        if (m_evaluation is { succeeded: false })
        {
            NativeImGui.Dummy(new Vector2(0f, 8f));
            NativeImGui.SeparatorText("Needs attention");
            DrawEvaluationStatus();
        }
        NativeImGui.Dummy(new Vector2(0f, 10f));
        if (NativeImGui.CollapsingHeader("Advanced"))
        {
            DrawTechniquePicker();
            NativeImGui.Dummy(new Vector2(0f, 4f));
            if (NativeImGui.Button("Reset graph from shader", new Vector2(-1f, 0f)))
                RebuildMapping();
        }
    }

    private void DrawShaderPicker()
    {
        string preview = m_asset?.shader is ShaderAsset shader
            ? string.IsNullOrWhiteSpace(shader.name) ? shader.assetPath.ToString() : shader.name
            : "Select Shader...";
        NativeImGui.TextUnformatted("Shader");
        if (!EditorWidget.BeginBoundedCombo("##material_graph_shader", preview))
            return;
        try
        {
            foreach (AssetFileEntry entry in m_assets.GetFileSystemEntries(includeDirectories: false)
                         .OrderBy(static value => value.assetPath.ToString(), StringComparer.Ordinal))
            {
                if (!m_assets.TryLoad(entry.assetPath, out ShaderAsset? candidate) || candidate is null)
                    continue;
                bool selected = ReferenceEquals(candidate, m_asset?.shader);
                if (NativeImGui.Selectable(entry.assetPath.ToString(), selected))
                    SelectShader(candidate);
            }
        }
        finally
        {
            NativeImGui.EndCombo();
        }
    }

    private void DrawTechniquePicker()
    {
        ShaderDefinition? definition = m_asset?.shader?.definition;
        if (definition is null)
            return;
        ShaderTechniqueId selectedTechnique = MaterialGraphDocumentModel.ReadTechniqueId(
            m_controller!.document,
            m_serialization);
        string preview = selectedTechnique.isValid ? selectedTechnique.value : "Automatic";
        NativeImGui.TextUnformatted("Technique");
        if (!EditorWidget.BeginBoundedCombo("##material_graph_technique", preview))
            return;
        try
        {
            if (NativeImGui.Selectable("Automatic", !selectedTechnique.isValid))
                SetTechnique(default);
            foreach (ShaderTechniqueDefinition technique in definition.techniques
                         .OrderBy(static value => value.id.value, StringComparer.Ordinal))
            {
                if (NativeImGui.Selectable(
                        technique.id.value,
                        technique.id == selectedTechnique))
                {
                    SetTechnique(technique.id);
                }
            }
        }
        finally
        {
            NativeImGui.EndCombo();
        }
    }

    private void DrawEvaluationStatus()
    {
        if (m_evaluation is null)
            return;
        Vector4 color = m_evaluation.succeeded
            ? new Vector4(0.42f, 0.78f, 0.56f, 1f)
            : new Vector4(0.96f, 0.38f, 0.38f, 1f);
        NativeImGui.PushStyleColor(ImGuiCol.Text, color);
        NativeImGui.TextWrapped(m_evaluation.succeeded
            ? $"Ready · {m_evaluation.properties.Count} mapped value(s)"
            : "Material mapping is invalid");
        NativeImGui.PopStyleColor();
        foreach (GraphDiagnostic diagnostic in m_evaluation.diagnostics.Take(5))
        {
            NativeImGui.TextWrapped($"{diagnostic.code}: {diagnostic.message}");
        }
    }

    private void DrawCanvas()
    {
        Vector2 origin = NativeImGui.GetCursorScreenPos();
        Vector2 size = NativeImGui.GetContentRegionAvail();
        size.X = MathF.Max(1f, size.X);
        size.Y = MathF.Max(1f, size.Y);
        _ = NativeImGui.InvisibleButton(
            "##MaterialGraphCanvas",
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
                    GraphPortDefinition outputPort = RequirePort(output);
                    if (StringComparer.Ordinal.Equals(outputPort.valueTypeId, definition.valueTypeId))
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
            if (!m_nodes.TryResolve(node.definitionId, out GraphNodeDefinition? definition)
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
        uint color = Pack(0.58f, 0.42f, 0.82f, 1f);
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
            bool resolved = m_nodes.TryResolve(node.definitionId, out GraphNodeDefinition? definition)
                && definition is not null;
            Rect rect = NodeRect(node, origin);
            bool selected = m_canvas.selectedNodes.Contains(node.id);
            uint body = resolved
                ? Pack(0.105f, 0.11f, 0.125f, 0.99f)
                : Pack(0.28f, 0.08f, 0.09f, 0.98f);
            drawList.AddRectFilled(
                rect.min + new Vector2(3f, 5f) * m_canvas.zoom,
                rect.max + new Vector2(5f, 8f) * m_canvas.zoom,
                Pack(0f, 0f, 0f, 0.24f),
                8f * m_canvas.zoom);
            drawList.AddRectFilled(rect.min, rect.max, body, 7f * m_canvas.zoom);
            drawList.AddRect(
                rect.min,
                rect.max,
                selected ? Pack(0.61f, 0.43f, 0.86f, 1f) : Pack(0.22f, 0.23f, 0.27f, 1f),
                7f * m_canvas.zoom,
                ImDrawFlags.None,
                selected ? 2.5f : 1f);
            drawList.AddRectFilled(
                rect.min,
                new Vector2(rect.max.X, rect.min.Y + C_NODE_HEADER_HEIGHT * m_canvas.zoom),
                resolved
                    ? node.definitionId == MaterialGraphIds.outputNode
                        ? Pack(0.28f, 0.21f, 0.39f, 1f)
                        : Pack(0.16f, 0.17f, 0.20f, 1f)
                    : Pack(0.5f, 0.12f, 0.13f, 1f),
                7f * m_canvas.zoom);
            drawList.AddText(
                rect.min + new Vector2(10f, 7f) * m_canvas.zoom,
                Pack(0.93f, 0.95f, 1f, 1f),
                resolved ? GetNodeTitle(node, definition!) : $"Missing: {node.definitionId}");
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

    private void DrawSelectedNodeEditor()
    {
        GraphNodeRecord? node = m_canvas.selectedNodes.Count == 1
            ? m_controller!.document.FindNode(m_canvas.selectedNodes.First())
            : null;
        if (node is null)
        {
            NativeImGui.TextWrapped("Select one property node to edit its mapped value.");
            return;
        }
        NativeImGui.TextUnformatted(GetNodeTitle(node, null));
        if (node.definitionId == MaterialGraphIds.outputNode)
        {
            NativeImGui.TextWrapped("The output mirrors material-owned properties from the selected shader. Connect a value node to override a shader default.");
            return;
        }
        MaterialValue value = MaterialGraphNodeResolver.ReadValue(node, m_serialization);
        bool changed = value.kind switch
        {
            MaterialValueKind.Float => DrawFloatValue(ref value),
            MaterialValueKind.Vector => DrawVectorValue(ref value),
            MaterialValueKind.Color => DrawColorValue(ref value),
            MaterialValueKind.Texture => DrawTextureValue(ref value),
            MaterialValueKind.Matrix => DrawMatrixValue(),
            _ => false
        };
        if (changed)
        {
            m_controller!.SetNodeValue(
                node.id,
                "value",
                GraphSerializedValue.From(value, m_serialization));
        }
    }

    private static bool DrawFloatValue(ref MaterialValue value)
    {
        float scalar = value.vector.x;
        if (!EditorWidget.CompactDragFloat("##material_value_float", ref scalar, 0.02f))
            return false;
        value = MaterialValue.FromFloat(scalar);
        return true;
    }

    private static bool DrawVectorValue(ref MaterialValue value)
    {
        Inno.Core.Mathematics.Vector4 vector = value.vector;
        bool changed = EditorWidget.AxisDragFloat("material_value", "X", ref vector.x, 38f, 0.02f);
        NativeImGui.SameLine();
        changed |= EditorWidget.AxisDragFloat("material_value", "Y", ref vector.y, 38f, 0.02f);
        changed |= EditorWidget.AxisDragFloat("material_value", "Z", ref vector.z, 38f, 0.02f);
        NativeImGui.SameLine();
        changed |= EditorWidget.AxisDragFloat("material_value", "W", ref vector.w, 38f, 0.02f);
        if (changed)
            value = MaterialValue.FromVector(vector);
        return changed;
    }

    private static bool DrawColorValue(ref MaterialValue value)
    {
        Vector4 color = new(value.vector.x, value.vector.y, value.vector.z, value.vector.w);
        if (!NativeImGui.ColorEdit4("##material_value_color", ref color))
            return false;
        value = MaterialValue.FromColor(new Inno.Core.Mathematics.Color(color.X, color.Y, color.Z, color.W));
        return true;
    }

    private bool DrawTextureValue(ref MaterialValue value)
    {
        string preview = value.texture?.assetPath.ToString() ?? "None";
        if (!EditorWidget.BeginBoundedCombo("##material_value_texture", preview))
            return false;
        bool changed = false;
        try
        {
            if (NativeImGui.Selectable("None", value.texture is null))
            {
                value = new MaterialValue { kind = MaterialValueKind.Texture, sampler = RenderSamplerState.linearClamp };
                changed = true;
            }
            foreach (AssetFileEntry entry in m_assets.GetFileSystemEntries(includeDirectories: false)
                         .OrderBy(static item => item.assetPath.ToString(), StringComparer.Ordinal))
            {
                if (!m_assets.TryLoad(entry.assetPath, out TextureAsset? texture) || texture is null)
                    continue;
                if (NativeImGui.Selectable(entry.assetPath.ToString(), ReferenceEquals(texture, value.texture)))
                {
                    value = MaterialValue.FromTexture(texture, value.sampler);
                    changed = true;
                }
            }
        }
        finally
        {
            NativeImGui.EndCombo();
        }
        return changed;
    }

    private static bool DrawMatrixValue()
    {
        NativeImGui.TextWrapped("Matrix values retain the reflected shader default. Matrix node editing is intentionally read-only in the compact blackboard.");
        return false;
    }

    private void EvaluateIfChanged()
    {
        if (m_controller!.revision == m_lastCompiledRevision)
            return;
        SynchronizeMaterialSelection();
        m_evaluation = MaterialGraphEvaluator.Evaluate(
            m_asset!,
            m_controller.document,
            m_serialization);
        m_lastCompiledRevision = m_controller.revision;
    }

    private void SetTechnique(ShaderTechniqueId techniqueId)
    {
        GraphNodeRecord output = m_controller!.document.nodes.Single(static node =>
            node.definitionId == MaterialGraphIds.outputNode);
        m_controller.SetNodeValue(
            output.id,
            "techniqueId",
            GraphSerializedValue.From(techniqueId.value ?? string.Empty, m_serialization));
    }

    private void SynchronizeMaterialSelection()
    {
        Guid shaderId = MaterialGraphDocumentModel.ReadShaderId(
            m_controller!.document,
            m_serialization);
        ShaderAsset? shader = null;
        if (shaderId != Guid.Empty)
            _ = m_assets.TryLoad(shaderId, out shader);
        if (!ReferenceEquals(m_asset!.shader, shader))
        {
            m_asset.shader = shader;
            m_nodes = new MaterialGraphNodeResolver(shader, m_serialization);
        }
        m_asset.techniqueId = MaterialGraphDocumentModel.ReadTechniqueId(
            m_controller.document,
            m_serialization);
    }

    private bool TrySave(EditorContext context)
    {
        try
        {
            Save(context);
            m_statusMessage = $"Saved material mapping to '{m_documentPath}'.";
            m_statusIsError = false;
            return true;
        }
        catch (Exception exception)
        {
            m_statusMessage = exception.Message;
            m_statusIsError = true;
            return false;
        }
    }

    private void Save(EditorContext context)
    {
        _ = context;
        MaterialAsset asset = m_asset
            ?? throw new InvalidOperationException("The Material Graph source is unavailable.");
        string? previousDocument = asset.TryGetMetadata(
            MaterialGraphDocumentStore.metadataKey,
            out string? storedDocument)
                ? storedDocument
                : null;
        ShaderAsset? previousShader = asset.shader;
        ShaderTechniqueId previousTechnique = asset.techniqueId;
        MaterialPropertyEntry[] previousProperties = asset.properties.ToArray();
        try
        {
            _ = MaterialGraphEvaluator.Commit(asset, m_controller!.document, m_serialization);
            MaterialGraphDocumentStore.Write(asset, m_controller.document, m_serialization);
            if (!m_assets.Save(AssetPath.Parse(m_documentPath), asset))
                throw new InvalidOperationException($"No Material importer can save '{m_documentPath}'.");
        }
        catch
        {
            asset.shader = previousShader;
            asset.techniqueId = previousTechnique;
            asset.ReplaceProperties(previousProperties);
            if (previousDocument is not null)
                asset.SetMetadata(MaterialGraphDocumentStore.metadataKey, previousDocument);
            else
                _ = asset.RemoveMetadata(MaterialGraphDocumentStore.metadataKey);
            throw;
        }
        m_sourceAssigned = true;
        m_controller.MarkSaved();
        CaptureAssetSnapshot(asset);
    }

    private void RequestNew()
    {
        if (m_controller?.isDirty == true)
        {
            m_pendingAction = PendingDocumentAction.New;
            m_pendingAsset = null;
            NativeImGui.OpenPopup(C_UNSAVED_POPUP);
            return;
        }
        NewDocument();
    }

    private void RequestOpen(MaterialAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.identity.persistentId == m_documentId)
            return;
        if (m_controller?.isDirty == true)
        {
            if (m_pendingAction != PendingDocumentAction.Open
                || !ReferenceEquals(m_pendingAsset, asset))
            {
                m_pendingAction = PendingDocumentAction.Open;
                m_pendingAsset = asset;
                NativeImGui.OpenPopup(C_UNSAVED_POPUP);
            }
            return;
        }
        Open(asset);
    }

    private void ExecutePendingAction()
    {
        PendingDocumentAction action = m_pendingAction;
        MaterialAsset? asset = m_pendingAsset;
        ClearPendingAction();
        if (action == PendingDocumentAction.New)
            NewDocument();
        else if (action == PendingDocumentAction.Open && asset is not null)
            Open(asset);
    }

    private void ClearPendingAction()
    {
        m_pendingAction = PendingDocumentAction.None;
        m_pendingAsset = null;
    }

    private MaterialAsset CreateNewAsset()
    {
        var asset = new MaterialAsset();
        m_assets.identities.InitializePersistentIdentity(asset, m_documentId);
        return asset;
    }

    private void NewDocument()
    {
        if (m_controller is not null)
            _ = m_graphs.CloseDocument(m_documentId);
        m_documentId = Guid.NewGuid();
        m_documentPath = C_DEFAULT_PATH;
        m_sourceAssigned = false;
        m_asset = CreateNewAsset();
        CaptureAssetSnapshot(m_asset);
        m_nodes = new MaterialGraphNodeResolver(null, m_serialization);
        m_controller = m_graphs.OpenDocument(
            m_documentId,
            MaterialGraphDocumentFactory.Create((ShaderAsset?)null, m_serialization),
            m_interactions.history);
        SelectFirstPropertyNode();
        m_canvas.SetViewport(new GraphPosition(420f, 80f), 1f);
        m_lastCompiledRevision = ulong.MaxValue;
        m_interactions.SetSelection(m_asset);
        m_statusMessage = "New material mapping created. Select a shader, then save.";
        m_statusIsError = false;
    }

    private void Open(MaterialAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (m_controller is not null && m_documentId != asset.identity.persistentId)
            _ = m_graphs.CloseDocument(m_documentId);
        m_documentId = asset.identity.persistentId;
        m_documentPath = asset.assetPath.ToString();
        m_sourceAssigned = true;
        m_asset = asset;
        CaptureAssetSnapshot(asset);
        m_nodes = new MaterialGraphNodeResolver(asset.shader, m_serialization);
        if (!m_graphs.TryOpenDocument(m_documentId, m_interactions.history, out m_controller))
        {
            m_controller = m_graphs.OpenDocument(
                m_documentId,
                MaterialGraphDocumentStore.ReadOrCreate(asset, m_serialization),
                m_interactions.history);
        }
        SelectFirstPropertyNode();
        m_lastCompiledRevision = ulong.MaxValue;
        m_statusMessage = $"Opened '{m_documentPath}'.";
        m_statusIsError = false;
    }

    private void CaptureAssetSnapshot(MaterialAsset? asset)
    {
        if (asset is null)
        {
            m_savedShader = null;
            m_savedTechnique = default;
            m_savedProperties = [];
            m_savedDocumentData = null;
            return;
        }
        m_savedShader = asset.shader;
        m_savedTechnique = asset.techniqueId;
        m_savedProperties = asset.properties.ToArray();
        m_savedDocumentData = asset.TryGetMetadata(
            MaterialGraphDocumentStore.metadataKey,
            out string? stored)
                ? stored
                : null;
    }

    private void RestoreAssetSnapshot()
    {
        if (m_asset is null)
            return;
        m_asset.shader = m_savedShader;
        m_asset.techniqueId = m_savedTechnique;
        m_asset.ReplaceProperties(m_savedProperties);
        if (m_savedDocumentData is not null)
            m_asset.SetMetadata(MaterialGraphDocumentStore.metadataKey, m_savedDocumentData);
        else
            _ = m_asset.RemoveMetadata(MaterialGraphDocumentStore.metadataKey);
    }

    private void SelectShader(ShaderAsset shader)
    {
        ArgumentNullException.ThrowIfNull(shader);
        GraphDocument replacement = MaterialGraphDocumentFactory.Create(
            shader,
            m_serialization,
            m_controller!.document);
        m_asset!.shader = shader;
        m_asset.techniqueId = default;
        m_nodes = new MaterialGraphNodeResolver(shader, m_serialization);
        m_controller.ReplaceDocument(replacement, "Change Material Shader");
        SelectFirstPropertyNode();
        m_canvas.SetViewport(new GraphPosition(420f, 80f), 1f);
        m_lastCompiledRevision = ulong.MaxValue;
    }

    private void RebuildMapping()
    {
        GraphDocument replacement = MaterialGraphDocumentFactory.Create(
            m_asset?.shader,
            m_serialization,
            m_controller!.document);
        m_controller.ReplaceDocument(replacement, "Rebuild Material Mapping");
        SelectFirstPropertyNode();
        m_canvas.SetViewport(new GraphPosition(420f, 80f), 1f);
        m_lastCompiledRevision = ulong.MaxValue;
    }

    private void SelectFirstPropertyNode()
    {
        GraphNodeRecord? first = m_controller?.document.nodes.FirstOrDefault(static node =>
            node.definitionId == MaterialGraphIds.valueNode);
        if (first is null)
            m_canvas.ClearSelection();
        else
            m_canvas.SelectNodes([first.id]);
    }

    private GraphPortDefinition RequirePort(GraphEndpoint endpoint)
    {
        GraphNodeRecord node = m_controller!.document.FindNode(endpoint.nodeId)
            ?? throw new InvalidOperationException($"Graph node '{endpoint.nodeId}' is unavailable.");
        if (!m_nodes.TryResolve(node.definitionId, out GraphNodeDefinition? definition)
            || definition is null)
        {
            throw new InvalidOperationException($"Graph node definition '{node.definitionId}' is unavailable.");
        }

        return definition.GetPorts(node).First(port => port.id == endpoint.portId);
    }

    private enum PendingDocumentAction
    {
        None,
        New,
        Open
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
        if (m_nodes.TryResolve(node.definitionId, out GraphNodeDefinition? definition)
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

    private string GetNodeTitle(GraphNodeRecord node, GraphNodeDefinition? definition)
    {
        if (node.definitionId == MaterialGraphIds.valueNode
            && node.TryGetValue("displayName", out GraphSerializedValue? name))
        {
            return name!.Deserialize<string>(m_serialization);
        }
        return definition?.displayName ?? "Material Output";
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
