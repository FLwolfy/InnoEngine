using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Build.Toolchains.Bgfx.Tools;
using Inno.Core.Diagnostics;
using Inno.Core.Graphs;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Core.Events;
using Inno.Core.Input;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Graph;
using Inno.Editor.Interactions;
using Inno.Editor.Panel.FileBrowser;
using Inno.Editor.Shaders;
using Inno.Editor.Rendering;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Reload;
using Inno.Extensibility.Types;
using Inno.Rendering;
using Inno.Rendering.Assets;
using Inno.Rendering.Shaders;
using Inno.Native.ImGui;
using UI = Inno.Native.ImGui.ImGui;
using Xunit;

namespace Inno.Editor.Scripting.Tests;

public sealed class ShaderEditorWorkflowTests : IDisposable
{
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "InnoShaderEditorWorkflow", Guid.NewGuid().ToString("N"));
    private readonly IdentityAllocator m_identities = new();
    private readonly IDisposable m_identityScope;
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;
    private readonly LogRouter m_logs = new();
    private readonly WorkflowLogs m_messages = new();
    private readonly DiagnosticReporter m_reporter;
    private readonly AssetPipeline m_assets;
    private readonly EditorRenderTargetArtifactProvider m_artifacts;
    private EditorInteractionRuntime m_runtime;
    private readonly WorkflowSink m_sink = new();
    private readonly ShaderGraphSourceStore m_source;
    private readonly ImGuiContextPtr m_imgui;
    private Vector2 m_canvasMinimum;
    private Vector2 m_canvasMaximum;
    private Vector2 m_probeOffset;

    public ShaderEditorWorkflowTests()
    {
        Directory.CreateDirectory(Path.Combine(m_root, "Assets"));
        m_logs.RegisterSink(m_messages);
        m_identityScope = m_identities.EnterScope();
        _ = typeof(ShaderNodeDrawer);
        _ = typeof(ShaderGraphSourceStore);
        _ = typeof(GraphEditorModule);
        m_modules = new(new() { cacheDirectory = Path.Combine(m_root, "Library", "Assemblies") });
        m_types = new(m_modules);
        m_serialization = new(m_types);
        var diagnostics = new DiagnosticHub();
        m_reporter = diagnostics.CreateReporter(new("tests.shader-editor", "Shader Editor workflow"));
        m_assets = new(m_modules, m_types, m_serialization, m_identities, diagnostics, m_logs,
            AssetPipelineOptions.Create(Path.Combine(m_root, "Assets"), Path.Combine(m_root, "Library")) with { enableFileSystemWatcher = false });
        m_source = new(m_assets, m_serialization);
        m_artifacts = new(m_assets, m_serialization, m_types, new ShaderCompiler(new WorkflowCompiler()), new BgfxTextureTargetCompiler(), m_reporter);
        m_runtime = CreateRuntime();
        m_imgui = UI.CreateContext();
        ImGuiIOPtr io = UI.GetIO();
        io.DisplaySize = new(1200, 800);
        io.DeltaTime = 1f / 60;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
        io.Fonts.RendererHasTextures = true;
    }

    [Fact]
    public void ParameterPresentationRoundTripsWithoutChangingTheRuntimeDefinitionOrSemanticProgram()
    {
        AssetFileEntry entry = Create("Presentation.ishader");
        GraphDocument graph = m_source.Read(entry.assetPath).document;
        SerializationContext context = AssetSerializationContext.Create(m_assets);
        byte[] beforeDefinition = m_serialization.Serialize(ShaderGraphDocument.ReadDefinition(graph, m_serialization, context), context);
        string beforeHash = ShaderGraphArtifact.GetSemanticHash(ShaderGraphArtifact.Encode(graph, new Dictionary<GraphNodeId, byte[]>(), m_serialization), m_serialization);
        ShaderPropertyId id = new("stable.tint");
        Assert.True(ShaderParameterPresentation.Read(graph, id, m_serialization, context).visible);
        ShaderParameterPresentation.Write(graph, id, new()
        {
            group = "Surface", description = "Linear tint", visible = false, hasRange = true, minimum = -2, maximum = 8
        }, m_serialization, context);
        GraphDocument restored = GraphDocumentCodec.Decode(GraphDocumentCodec.Encode(graph, m_serialization), m_serialization);
        ShaderParameterPresentation presentation = ShaderParameterPresentation.Read(restored, id, m_serialization, context);
        Assert.Equal("Surface", presentation.group);
        Assert.Equal("Linear tint", presentation.description);
        Assert.False(presentation.visible);
        Assert.True(presentation.hasRange);
        Assert.Equal(-2, presentation.minimum);
        Assert.Equal(8, presentation.maximum);
        presentation.group = "Detached";
        Assert.Equal("Surface", ShaderParameterPresentation.Read(restored, id, m_serialization, context).group);
        Assert.Equal(beforeDefinition, m_serialization.Serialize(ShaderGraphDocument.ReadDefinition(restored, m_serialization, context), context));
        Assert.Equal(beforeHash, ShaderGraphArtifact.GetSemanticHash(ShaderGraphArtifact.Encode(restored, new Dictionary<GraphNodeId, byte[]>(), m_serialization), m_serialization));
    }

    [Fact]
    public void ParameterPresentationRejectsInvalidBoundsBeforeMutatingTheGraph()
    {
        GraphDocument graph = m_source.Read(Create("Range.ishader").assetPath).document;
        byte[] before = GraphDocumentCodec.Encode(graph, m_serialization);
        SerializationContext context = AssetSerializationContext.Create(m_assets);
        foreach (var range in new[] { (double.NaN, 1d), (0d, double.PositiveInfinity), (2d, 1d), (-1e100, 0d), (0d, 1e100) })
            Assert.Throws<ArgumentException>(() => ShaderParameterPresentation.Write(graph, new("range"),
                new() { hasRange = true, minimum = range.Item1, maximum = range.Item2 }, m_serialization, context));
        Assert.Equal(before, GraphDocumentCodec.Encode(graph, m_serialization));
    }

    [Fact]
    public void DiagnosticPopupUsesTheCanvasActionWithoutChangingFileSelectionOrHistory()
    {
        AssetFileEntry entry = Create("Diagnostics.ishader");
        SelectAndDraw(entry);
        byte[] before = File.ReadAllBytes(Path.Combine(m_root, "Assets", entry.assetPath.localPath));
        Assert.True(m_runtime.interactions.For("panel/rendering.shader-editor", entry).Execute("shader/diagnostics"));
        Draw();
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(m_root, "Assets", entry.assetPath.localPath)));
        Assert.Single(m_runtime.interactions.documents.documents);
    }

    [Fact]
    public void SourceSettingsUndoRedoRestoresValidAndInvalidImportStates()
    {
        AssetPath path = CreateFunction();
        var edits = new AssetImportSettingsEdits(m_assets, m_serialization, m_types, m_runtime.interactions);
        AssetImportSettingsSnapshot before = m_assets.GetImportSettings(path);
        var settings = Assert.IsType<ShaderSourceImportSettings>(before.value);
        settings.entryPoint = "MissingFunction";
        Assert.False(edits.Apply(path, settings, before.fingerprint));
        Assert.Equal("MissingFunction", Assert.IsType<ShaderSourceImportSettings>(m_assets.GetImportSettings(path).value).entryPoint);
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal("Evaluate", Assert.IsType<ShaderSourceImportSettings>(m_assets.GetImportSettings(path).value).entryPoint);
        Assert.True(m_assets.TryGetInfo(path, out AssetInfo? info));
        Assert.Equal(AssetImportStatus.Imported, info!.status);
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Equal("MissingFunction", Assert.IsType<ShaderSourceImportSettings>(m_assets.GetImportSettings(path).value).entryPoint);
    }

    [Fact]
    public void SourceSettingsUndoRefusesToOverwriteAnExternalSidecarEdit()
    {
        AssetPath path = CreateFunction();
        var edits = new AssetImportSettingsEdits(m_assets, m_serialization, m_types, m_runtime.interactions);
        AssetImportSettingsSnapshot before = m_assets.GetImportSettings(path);
        var settings = Assert.IsType<ShaderSourceImportSettings>(before.value);
        settings.entryPoint = "MissingFunction";
        _ = edits.Apply(path, settings, before.fingerprint);
        AssetImportSettingsSnapshot current = m_assets.GetImportSettings(path);
        var external = Assert.IsType<ShaderSourceImportSettings>(current.value);
        external.entryPoint = "ExternalFunction";
        _ = m_assets.SaveImportSettings(path, external, current.fingerprint);
        Assert.False(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal("ExternalFunction", Assert.IsType<ShaderSourceImportSettings>(m_assets.GetImportSettings(path).value).entryPoint);
    }

    private AssetPath CreateFunction()
    {
        AssetPath path = AssetPath.Project("Function.ishadersource");
        File.WriteAllText(Path.Combine(m_root, "Assets", path.localPath), "float Evaluate(float input) { return input; }");
        _ = m_assets.Import(path);
        var settings = new ShaderSourceImportSettings { languageId = "inno.shader-language.bgfx-sc", implementationId = "bgfx", entryPoint = "Evaluate" };
        Assert.True(m_assets.SaveImportSettings(path, settings, m_assets.GetImportSettings(path).fingerprint));
        return path;
    }

    [Fact]
    public void FileSelectionOpensOnlyTheShaderCanvasAndNonShaderSelectionKeepsItsDocument()
    {
        AssetFileEntry entry = Create("Surface.ishader");
        SelectAndDraw(entry);
        EditorDocumentContext document = Assert.Single(m_runtime.interactions.documents.documents);
        Assert.Equal(AssetId(entry), document.assetId);
        Assert.NotEqual(entry.identity.persistentId, document.assetId);
        Assert.DoesNotContain(m_runtime.panels, panel => panel.id == "editor.documents" && panel.isOpen);
        m_runtime.interactions.SetSelection(null);
        Draw();
        Assert.Same(document, Assert.Single(m_runtime.interactions.documents.documents));
        SelectAndDraw(entry);
        Assert.Single(m_runtime.interactions.documents.documents);
    }

    [Theory]
    [InlineData(1f, false)]
    [InlineData(1.1f, false)]
    [InlineData(1.25f, false)]
    [InlineData(1.5f, false)]
    [InlineData(2f, false)]
    [InlineData(1f, true)]
    [InlineData(1.1f, true)]
    [InlineData(1.25f, true)]
    [InlineData(1.5f, true)]
    [InlineData(2f, true)]
    public void NativeCanvasResizeCompletesLayoutAtFractionalUiScales(float scale, bool emptyGraph)
    {
        AssetFileEntry entry = Create("Resize.ishader");
        SelectAndDraw(entry);
        GraphDocumentController controller = Controller(entry);
        if (emptyGraph) controller.ReplaceDocument(new GraphDocument(), "Prepare Empty Canvas");
        ulong revision = controller.revision;
        byte[] disk = File.ReadAllBytes(Path.Combine(m_root, "Assets", entry.assetPath.localPath));
        UI.GetStyle().ItemSpacing = new Vector2(6, 4) * scale;
        UI.GetStyle().WindowPadding = new Vector2(8, 7) * scale;
        UI.GetIO().DisplayFramebufferScale = new(scale);

        Vector2[] sizes = [new(1200, 800), new(640, 400), new(280, 220), new(96, 64), new(32, 32),
            new(1200, 32), new(32, 800), new(1200, 800)];
        for (int cycle = 0; cycle < 3; cycle++)
            foreach (Vector2 size in sizes)
            {
                // Run both the resize frame and its settled layout with native assertions enabled.
                Draw(size);
                Draw(size);
            }

        Assert.Equal(revision, controller.revision);
        Assert.Equal(disk, File.ReadAllBytes(Path.Combine(m_root, "Assets", entry.assetPath.localPath)));
        Assert.Single(m_runtime.interactions.documents.documents);
    }

    [Fact]
    public void SeparateMovesAndUndoRedoRemainDraftsUntilExplicitSave()
    {
        AssetFileEntry entry = Create("Moves.ishader");
        SelectAndDraw(entry);
        GraphDocumentController graph = Controller(entry);
        GraphNodeId node = graph.document.nodes[0].id;
        GraphPosition original = graph.document.nodes[0].position;
        graph.MoveNodes(new Dictionary<GraphNodeId, GraphPosition> { [node] = new(111, 222) });
        Tick();
        Assert.Equal(original, ReadPosition(entry, node));
        Assert.True(Assert.Single(m_runtime.interactions.documents.documents).isDirty);
        graph.MoveNodes(new Dictionary<GraphNodeId, GraphPosition> { [node] = new(333, 444) });
        Tick();
        Assert.Equal(original, ReadPosition(entry, node));
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Tick();
        Assert.Equal(new(111, 222), graph.document.FindNode(node)!.position);
        Assert.Equal(original, ReadPosition(entry, node));
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Tick();
        Assert.Equal(original, ReadPosition(entry, node));
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Tick();
        Assert.Equal(new(111, 222), graph.document.FindNode(node)!.position);
        Assert.Equal(original, ReadPosition(entry, node));
        Assert.True(m_runtime.interactions.For("panel/rendering.shader-editor", entry).Execute("shader/save"));
        Assert.Equal(new(111, 222), ReadPosition(entry, node));
        Assert.False(Assert.Single(m_runtime.interactions.documents.documents).isDirty);
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Tick();
        Assert.Equal(original, graph.document.FindNode(node)!.position);
        Assert.Equal(new(111, 222), ReadPosition(entry, node));
    }

    [Fact]
    public void SwitchingSelectionKeepsAnInvalidDraftAndExplicitCloseSavePersistsIt()
    {
        AssetFileEntry first = Create("Incomplete.ishader"), second = Create("Other.ishader");
        SelectAndDraw(first);
        GraphNodeId missing = Controller(first).AddNode("tests.uninstalled.node", new(32, 64));
        Tick();
        SelectAndDraw(second);
        Assert.Null(m_source.Read(first.assetPath).document.FindNode(missing));
        Assert.NotNull(Controller(first).document.FindNode(missing));
        EditorDocumentContext firstDocument = m_runtime.interactions.documents.documents.Single(value => value.assetId == AssetId(first));
        Assert.True(m_runtime.interactions.documents.Close(firstDocument.documentId, EditorDocumentCloseMode.Save));
        SelectAndDraw(first);
        Assert.NotNull(Controller(first).document.FindNode(missing));
    }

    [Fact]
    public void NativeMouseGesturesMoveNodesAndEachReleaseHasItsOwnUndo()
    {
        AssetFileEntry entry = Create("Pointer.ishader");
        SelectAndDraw(entry);
        GraphDocumentController controller = Controller(entry);
        var graph = new GraphDocument();
        var node = new GraphNodeRecord(new("probe"), "tests.shader-ui-probe") { position = new(0, 0) };
        graph.AddNode(node);
        controller.ReplaceDocument(graph, "Prepare Canvas Probe");
        Assert.True(m_runtime.interactions.For("panel/rendering.shader-editor", entry).Execute("shader/focus"));
        Draw();
        Draw();
        GraphPosition original = controller.document.FindNode(node.id)!.position;
        DragProbe(new(52, 37));
        GraphPosition first = controller.document.FindNode(node.id)!.position;
        Assert.NotEqual(original, first);
        DragProbe(new(41, 23));
        GraphPosition second = controller.document.FindNode(node.id)!.position;
        Assert.NotEqual(first, second);
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Tick();
        Assert.Equal(first, controller.document.FindNode(node.id)!.position);
        Assert.Null(m_source.Read(entry.assetPath).document.FindNode(node.id));
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Tick();
        Assert.Equal(original, controller.document.FindNode(node.id)!.position);
    }

    private void DragProbe(Vector2 distance)
    {
        // A single framed summary-only node is centered in the canvas (82 px tall).
        Vector2 start = (m_canvasMinimum + m_canvasMaximum) * 0.5f + new Vector2(0, -25) + m_probeOffset;
        Assert.True(start.X > 0 && start.Y > 0);
        UI.GetIO().AddMousePosEvent(start.X, start.Y);
        Draw();
        UI.GetIO().AddMouseButtonEvent(0, true);
        Draw();
        UI.GetIO().AddMousePosEvent(start.X + distance.X, start.Y + distance.Y);
        Draw();
        UI.GetIO().AddMouseButtonEvent(0, false);
        Draw();
        Tick();
        m_probeOffset += distance;
    }

    [Fact]
    public void ExternalSourceConflictKeepsBothDiskAndUnsavedRecovery()
    {
        AssetFileEntry entry = Create("Conflict.ishader");
        SelectAndDraw(entry);
        GraphNodeId pending = Controller(entry).AddNode("tests.unsaved", new(40, 50));
        GraphDocument external = m_source.Read(entry.assetPath).document;
        external.AddNode(new(new("external"), "tests.external"));
        byte[] externalBytes = GraphDocumentCodec.Encode(external, m_serialization);
        File.WriteAllBytes(Path.Combine(m_root, "Assets", "Conflict.ishader"), externalBytes);
        Tick();
        Assert.False(m_runtime.interactions.documents.Save(Assert.Single(m_runtime.interactions.documents.documents).documentId));
        Assert.Equal(externalBytes, File.ReadAllBytes(Path.Combine(m_root, "Assets", "Conflict.ishader")));
        Assert.NotNull(Controller(entry).document.FindNode(pending));
        Assert.True(Assert.Single(m_runtime.interactions.documents.documents).isDirty);
        Assert.True(File.Exists(Path.Combine(m_assets.libraryRoot, "Editor", "ShaderRecovery", AssetId(entry).ToString("N") + ".inno")));
    }

    private AssetFileEntry Create(string name)
    {
        File.WriteAllBytes(Path.Combine(m_root, "Assets", name), GraphDocumentCodec.Encode(
            ShaderGraphTemplates.CreateRaster(m_serialization, AssetSerializationContext.Create(m_assets)), m_serialization));
        Assert.True(m_assets.Import(AssetPath.Project(name)));
        Assert.True(m_assets.TryGetFileSystemEntry(AssetPath.Project(name), out AssetFileEntry entry));
        return entry;
    }

    [Theory]
    [InlineData(KeyCode.Delete)]
    [InlineData(KeyCode.Backspace)]
    public void DeleteKeysRemoveTheSelectedComputeStageAndItsDeclarationsAsOneUndo(KeyCode key)
    {
        AssetFileEntry entry = Create("Delete.ishader");
        SelectAndDraw(entry);
        var interaction = m_runtime.interactions.For("panel/rendering.shader-editor", entry);
        Assert.True(interaction.Execute("shader/create-pass", "Compute"));
        GraphDocumentController controller = Controller(entry);
        GraphNodeRecord output = controller.document.nodes.Last();
        var input = new GraphNodeRecord(new("compute-parameter"), "inno.shader.stage-input");
        input.SetValue("stage", ShaderGraphDocument.Encode(output.id.value, m_serialization, AssetSerializationContext.Create(m_assets)));
        GraphDocument graph = controller.document.Clone();
        graph.AddNode(input);
        graph = ShaderGraphBindings.ChangeInput(graph, input.id, new() { id = "computeParameter", kind = ShaderIrInputKind.Uniform }, m_serialization, AssetSerializationContext.Create(m_assets));
        controller.ReplaceDocument(graph, "Add Compute Parameter");
        byte[] before = GraphDocumentCodec.Encode(controller.document, m_serialization);
        Assert.True(interaction.Query("shader/delete").isEnabled);
        Assert.True(interaction.Query("shader/cut").isEnabled, "Stage selection was lost before keyboard dispatch.");
        Assert.True(interaction.TryGetShortcut("shader/delete", out HotKeyGesture deleteGesture));
        Assert.Equal(KeyCode.Delete, deleteGesture.key);
        interaction.Focus();
        Assert.Equal("panel/rendering.shader-editor", m_runtime.interactions.focusedArea);
        var keyEvent = new KeyPressedEvent(0, key);
        m_runtime.HandleKeyPressed(keyEvent);
        m_logs.Flush();
        Assert.True(controller.document.FindNode(output.id) is null, "Focus: " + m_runtime.interactions.focusedArea + "; cut: " + interaction.Query("shader/cut").isEnabled + "; undo: " + m_runtime.interactions.history.undoName + "; " + string.Join("\n", m_messages.messages));
        Assert.Null(controller.document.FindNode(input.id));
        ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(controller.document, m_serialization, AssetSerializationContext.Create(m_assets));
        Assert.Single(definition.passes);
        Assert.DoesNotContain(definition.properties, property => property.id.value == "computeParameter");
        using var nodes = new ShaderNodeCompilerRegistry(m_types);
        Assert.True(new ShaderGraphProgramCompiler(nodes).Lower(controller.document, "bgfx", new Dictionary<GraphNodeId, ShaderSourceModuleAnalysis>(),
            m_serialization, AssetSerializationContext.Create(m_assets)).succeeded);
        Tick();
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal(before, GraphDocumentCodec.Encode(controller.document, m_serialization));
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Null(controller.document.FindNode(output.id));
    }

    [Fact]
    public void CutAndPasteStageKeepsItsContentsAndDoesNotCreateDuplicatePassDeclarations()
    {
        AssetFileEntry entry = Create("Clipboard.ishader");
        SelectAndDraw(entry);
        var interaction = m_runtime.interactions.For("panel/rendering.shader-editor", entry);
        Assert.True(interaction.Execute("shader/create-pass", "Compute"));
        GraphDocumentController controller = Controller(entry);
        GraphNodeId stage = controller.document.nodes.Last().id;
        GraphNodeId child = controller.AddNode("inno.shader.constant", new(10, 20), new Dictionary<string, GraphSerializedValue>
        { ["stage"] = ShaderGraphDocument.Encode(stage.value, m_serialization, AssetSerializationContext.Create(m_assets)) });
        Assert.True(interaction.Execute("shader/cut"));
        Assert.Null(controller.document.FindNode(child));
        Assert.True(interaction.Execute("shader/paste"));
        Assert.Null(controller.document.FindNode(child));
        Assert.Equal(2, controller.document.nodes.Count(node => node.definitionId == "inno.shader.stage-output" && ReadStage(node).stage != ShaderStage.Compute));
        GraphNodeRecord pasted = controller.document.nodes.Single(node => node.definitionId == "inno.shader.stage-output" && ReadStage(node).stage == ShaderStage.Compute);
        Assert.Contains(controller.document.nodes, node => ShaderGraphDocument.Read(node, "stage", "", m_serialization, AssetSerializationContext.Create(m_assets)) == pasted.id.value);
        Assert.True(interaction.Execute("shader/duplicate"));
        ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(controller.document, m_serialization, AssetSerializationContext.Create(m_assets));
        Assert.Equal(3, definition.passes.Length);
        Assert.Equal(3, definition.passes.Select(pass => pass.name).Distinct().Count());
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal(2, ShaderGraphDocument.ReadDefinition(controller.document, m_serialization, AssetSerializationContext.Create(m_assets)).passes.Length);
        ShaderGraphStageSettings ReadStage(GraphNodeRecord node) => ShaderGraphDocument.Read(node, "settings", new ShaderGraphStageSettings(), m_serialization, AssetSerializationContext.Create(m_assets));
    }

    [Fact]
    public void SourceMenuUsesAssetIdentityAndDisconnectCommitsOneUndoWithoutSaving()
    {
        AssetPath source = CreateFunction();
        AssetFileEntry entry = Create("Source.ishader");
        SelectAndDraw(entry);
        var interaction = m_runtime.interactions.For("panel/rendering.shader-editor", entry);
        EditorMenuItem create = interaction.BuildMenu().items.Single(item => item.label == "Create");
        EditorMenuItem functions = create.children.Single(item => item.label == "Source Functions");
        EditorMenuItem function = Assert.Single(functions.children);
        Assert.True(interaction.Execute(function.actionId!, function.argument));
        GraphDocumentController controller = Controller(entry);
        GraphNodeRecord node = controller.document.nodes.Single(node => node.definitionId == "inno.shader.source");
        Assert.True(m_assets.TryGetInfo(source, out AssetInfo? info));
        Assert.Equal(info!.persistentId, ShaderGraphDocument.Read(node, "sourceId", Guid.Empty, m_serialization, AssetSerializationContext.Create(m_assets)));
        Tick();
        using var nodes = new ShaderNodeCompilerRegistry(m_types);
        using var frontends = new ShaderSourceFrontendRegistry(m_types);
        ShaderFunctionAsset functionAsset = m_assets.Load<ShaderFunctionAsset>(source);
        ShaderSourceModuleAnalysis module = frontends.AnalyzeModule(ShaderSourceBundle.Decode(ShaderSourceBundle.Read(functionAsset, m_assets), m_serialization));
        IReadOnlyList<ShaderNodePort> ports = nodes.DescribePorts(node, m_serialization, AssetSerializationContext.Create(m_assets), module, functionAsset.implementationId, null);
        Assert.Contains(ports, port => port.direction == GraphPortDirection.Input);
        Assert.Contains(ports, port => port.direction == GraphPortDirection.Output);
        GraphNodeId constant = controller.AddNode("inno.shader.constant", new(0, 0));
        GraphDocument candidate = controller.document.Clone();
        var edge = new GraphEdgeRecord(new("source-link"), new(constant, new("value")), new(node.id, new(ports.First(port => port.direction == GraphPortDirection.Input).id)));
        candidate.AddEdge(edge);
        controller.ReplaceDocument(candidate, "Connect Source");
        Assert.True(interaction.Execute("shader/disconnect"));
        Assert.DoesNotContain(controller.document.edges, value => value.id == edge.id);
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Contains(controller.document.edges, value => value.id == edge.id);
        Assert.Null(m_source.Read(entry.assetPath).document.FindNode(node.id));
    }

    [Fact]
    public void SavingAppliesTheGraphWithoutDependingOnFileWatcherAndUndoStaysDraftOnly()
    {
        AssetFileEntry entry = Create("Apply.ishader");
        SelectAndDraw(entry);
        ShaderAsset asset = m_assets.Load<ShaderAsset>(AssetId(entry));
        long before = asset.contentVersion;
        GraphDocumentController controller = Controller(entry);
        GraphDocument graph = controller.document.Clone();
        ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(graph, m_serialization, AssetSerializationContext.Create(m_assets));
        definition.name = "Saved Change";
        graph.SetMetadata(ShaderGraphDocument.definitionKey, ShaderGraphDocument.Encode(m_serialization.Serialize(definition, AssetSerializationContext.Create(m_assets)), m_serialization, AssetSerializationContext.Create(m_assets)));
        controller.ReplaceDocument(graph, "Rename Shader");
        Tick();
        Assert.Equal(before, asset.contentVersion);
        var interaction = m_runtime.interactions.For("panel/rendering.shader-editor", entry);
        interaction.Focus();
        HotKeyGesture save = HotKeyGesture.Primary(KeyCode.S);
        m_runtime.HandleKeyPressed(new KeyPressedEvent(0, save.key, save.modifiers));
        Tick();
        Assert.True(m_assets.TryLoad(AssetId(entry), out ShaderAsset? saved));
        Assert.Equal("Saved Change", saved!.definition!.name);
        Assert.True(saved.contentVersion > before);
        long savedVersion = saved.contentVersion;
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Tick();
        Assert.Equal("Saved Change", saved.definition!.name);
        Assert.Equal(savedVersion, saved.contentVersion);
        Assert.True(interaction.Execute("shader/save"));
        Tick();
        Assert.True(m_assets.TryLoad(AssetId(entry), out ShaderAsset? undone));
        Assert.NotEqual("Saved Change", undone!.definition!.name);
    }

    [Fact]
    public void CancelKeepsDraftAndDiscardRestoresDiskWithoutApplyingIt()
    {
        AssetFileEntry entry = Create("Close.ishader");
        SelectAndDraw(entry);
        GraphNodeId node = Controller(entry).AddNode("tests.unsaved", new(0, 0));
        Tick();
        EditorDocumentContext document = Assert.Single(m_runtime.interactions.documents.documents);
        Assert.False(m_runtime.interactions.documents.Close(document.documentId, EditorDocumentCloseMode.Cancel));
        Assert.NotNull(Controller(entry).document.FindNode(node));
        Assert.True(m_runtime.interactions.documents.Close(document.documentId, EditorDocumentCloseMode.Discard));
        SelectAndDraw(entry);
        Assert.Null(Controller(entry).document.FindNode(node));
        Assert.Null(m_source.Read(entry.assetPath).document.FindNode(node));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestartRecoversOnlyTheLatestDraftAndNeverAppliesIt(bool undo)
    {
        AssetFileEntry entry = Create("Recovery.ishader");
        SelectAndDraw(entry);
        byte[] saved = File.ReadAllBytes(Path.Combine(m_root, "Assets", entry.assetPath.localPath));
        GraphNodeId node = Controller(entry).AddNode("tests.unsaved", new(0, 0));
        Tick();
        if (undo) Assert.True(m_runtime.interactions.history.Undo().succeeded);
        // Shutdown must capture even the final undo that has not had an update frame yet.
        m_runtime.Dispose();
        Assert.Equal(saved, File.ReadAllBytes(Path.Combine(m_root, "Assets", entry.assetPath.localPath)));
        m_runtime = CreateRuntime();
        SelectAndDraw(entry);
        Assert.Equal(!undo, Controller(entry).document.FindNode(node) is not null);
        Assert.Equal(saved, File.ReadAllBytes(Path.Combine(m_root, "Assets", entry.assetPath.localPath)));
    }

    [Fact]
    public async Task DraftCompilationNeverReplacesTheCanonicalArtifactAndLayoutChangesDoNotRecompile()
    {
        AssetFileEntry entry = Create("Preview.ishader");
        ShaderAsset shader = m_assets.Load<ShaderAsset>(entry.assetPath);
        byte[] disk = File.ReadAllBytes(Path.Combine(m_root, "Assets", entry.assetPath.localPath));
        RenderTextureFormat[] formats = Enum.GetValues<RenderTextureFormat>();
        var capabilities = new GraphicsCapabilities(GraphicsApi.Metal, GraphicsCapability.None, new(256, 8, 8192, 16), formats, formats, formats, formats, false, false);
        var compilation = new EditorShaderCompilation(m_artifacts, capabilities);
        RenderShaderArtifact? canonical = null;
        for (int i = 0; i < 100 && canonical is null; i++)
        { _ = m_artifacts.GetShaderArtifact(shader, RenderShaderVariant.empty, capabilities, out canonical); await Task.Yield(); }
        Assert.NotNull(canonical);
        GraphDocument draft = m_source.Read(entry.assetPath).document;
        var context = AssetSerializationContext.Create(m_assets);
        ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(draft, m_serialization, context);
        ShaderPropertyDefinition property = definition.properties[0];
        property.defaultValue = MaterialValue.FromColor(new Inno.Core.Mathematics.Color(0.23f, 1, 1, 1));
        definition.properties[0] = property;
        draft.SetMetadata(ShaderGraphDocument.definitionKey, ShaderGraphDocument.Encode(m_serialization.Serialize(definition, context), m_serialization, context));
        Guid previewId = Guid.NewGuid();
        async Task<EditorShaderDraftCompilationSnapshot> Poll(ulong revision)
        {
            EditorShaderDraftCompilationSnapshot result = compilation.RequestDraft(previewId, draft, revision, RenderShaderVariant.empty);
            for (int i = 0; i < 100 && result.state == EditorShaderCompilationState.Compiling; i++)
            { await Task.Yield(); result = compilation.RequestDraft(previewId, draft, revision, RenderShaderVariant.empty); }
            return result;
        }
        var preview = await Poll(1);
        Assert.Equal(EditorShaderCompilationState.Succeeded, preview.state);
        Assert.NotNull(preview.artifact);
        Assert.NotEqual(canonical!.contentHash, preview.artifact!.contentHash);
        draft.nodes[0].position = new(123, 456);
        Assert.Same(preview.artifact, (await Poll(2)).artifact);
        GraphNodeRecord output = draft.nodes.First(value => value.definitionId == ShaderGraphDocument.outputDefinitionId);
        draft.RemoveNode(output.id);
        var failed = await Poll(3);
        Assert.Equal(EditorShaderCompilationState.Failed, failed.state);
        Assert.True(failed.usingLastGood);
        Assert.Same(preview.artifact, failed.artifact);
        _ = m_artifacts.GetShaderArtifact(shader, RenderShaderVariant.empty, capabilities, out var unchanged);
        Assert.Same(canonical, unchanged);
        Assert.Equal(disk, File.ReadAllBytes(Path.Combine(m_root, "Assets", entry.assetPath.localPath)));
        compilation.ReleaseDraft(previewId);
    }

    private EditorInteractionRuntime CreateRuntime()
    {
        RenderTextureFormat[] formats = Enum.GetValues<RenderTextureFormat>();
        var capabilities = new GraphicsCapabilities(GraphicsApi.Metal, GraphicsCapability.None, new(256, 8, 8192, 16), formats, formats, formats, formats, false, false);
        var runtime = new EditorInteractionRuntime(new EditorContext(m_root), m_types, m_logs,
            [m_types, m_serialization, m_assets, m_sink, new EditorReloadCoordinator(), new EditorShaderCompilation(m_artifacts, capabilities), new EmptyPreviews()]);
        runtime.Start();
        return runtime;
    }

    [Fact]
    public void MaterialGesturesAreIndependentAndOnlySavePublishesTheDraft()
    {
        (AssetPath path, ShaderPropertyId property) = CreateMaterial();
        MaterialDocuments documents = Assert.IsType<MaterialDocuments>(m_sink.materials);
        Guid id = documents.Open(path);
        byte[] original = File.ReadAllBytes(Path.Combine(m_root, "Assets", path.localPath));
        MaterialAsset canonical = m_assets.Load<MaterialAsset>(path);
        void Set(float value, bool finish = true)
        {
            MaterialAsset candidate = documents.Read(id);
            candidate.Set(property, MaterialValue.FromColor(new Inno.Core.Mathematics.Color(value, 1, 1, 1)));
            documents.Replace(id, candidate, finish);
        }
        Set(0.1f, false); Set(0.2f, false); documents.Commit(id);
        Set(0.3f, false); Set(0.4f, true);
        Assert.False(canonical.TryGet(property, out _));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(m_root, "Assets", path.localPath)));
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.True(documents.Read(id).TryGet(property, out MaterialValue value));
        Assert.Equal(0.2f, value.vector.x);
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.False(documents.Read(id).TryGet(property, out _));
        Assert.False(Assert.Single(m_runtime.interactions.documents.documents).isDirty);
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        EditorDocumentContext context = Assert.Single(m_runtime.interactions.documents.documents);
        Assert.True(m_runtime.interactions.documents.Save(context.documentId));
        Assert.False(canonical.TryGet(property, out _));
        Tick();
        Assert.True(canonical.TryGet(property, out value));
        Assert.Equal(0.2f, value.vector.x);
        Set(0.8f);
        Assert.True(m_runtime.interactions.documents.Revert(context.documentId));
        Assert.True(documents.Read(id).TryGet(property, out value));
        Assert.Equal(0.2f, value.vector.x);
    }

    [Fact]
    public void MaterialSaveConflictKeepsDiskAndRecoveredDraftDistinct()
    {
        (AssetPath path, ShaderPropertyId property) = CreateMaterial();
        MaterialDocuments documents = Assert.IsType<MaterialDocuments>(m_sink.materials);
        Guid id = documents.Open(path);
        MaterialAsset candidate = documents.Read(id);
        candidate.Set(property, MaterialValue.FromColor(new Inno.Core.Mathematics.Color(0.3f, 1, 1, 1)));
        documents.Replace(id, candidate);
        AssetSourceStore sources = m_assets.CreateSourceStore();
        AssetSourceSnapshot before = sources.Read(path);
        MaterialAsset external = sources.Decode<MaterialAsset>(before.bytes);
        external.Set(property, MaterialValue.FromColor(new Inno.Core.Mathematics.Color(0.9f, 1, 1, 1)));
        byte[] externalBytes = sources.Encode(external);
        sources.Save(path, externalBytes, before.contentHash);
        EditorDocumentContext context = Assert.Single(m_runtime.interactions.documents.documents);
        Assert.False(m_runtime.interactions.documents.Save(context.documentId));
        Assert.Equal(externalBytes, sources.Read(path).bytes);
        m_runtime.Dispose();
        m_runtime = CreateRuntime();
        documents = Assert.IsType<MaterialDocuments>(m_sink.materials);
        documents.Open(path);
        Assert.True(documents.Read(id).TryGet(property, out MaterialValue recovered));
        Assert.Equal(0.3f, recovered.vector.x);
        Assert.Equal(externalBytes, sources.Read(path).bytes);
    }

    [Fact]
    public void CleanMaterialFollowsExternalSourceButDirtyMaterialKeepsItsDraft()
    {
        (AssetPath path, ShaderPropertyId property) = CreateMaterial();
        MaterialDocuments documents = Assert.IsType<MaterialDocuments>(m_sink.materials);
        Guid id = documents.Open(path);
        void External(float value)
        {
            AssetSourceStore sources = m_assets.CreateSourceStore();
            AssetSourceSnapshot captured = sources.Read(path);
            MaterialAsset material = sources.Decode<MaterialAsset>(captured.bytes);
            material.Set(property, MaterialValue.FromColor(new Inno.Core.Mathematics.Color(value, 1, 1, 1)));
            sources.Save(path, sources.Encode(material), captured.contentHash);
            Assert.True(m_assets.Import(path));
            Tick();
        }
        External(0.2f);
        Assert.True(documents.Read(id).TryGet(property, out MaterialValue inherited));
        Assert.Equal(0.2f, inherited.vector.x);
        Assert.False(Assert.Single(m_runtime.interactions.documents.documents).isDirty);
        MaterialAsset candidate = documents.Read(id);
        candidate.Set(property, MaterialValue.FromColor(new Inno.Core.Mathematics.Color(0.5f, 1, 1, 1)));
        documents.Replace(id, candidate);
        External(0.8f);
        Assert.True(documents.Read(id).TryGet(property, out MaterialValue draft));
        Assert.Equal(0.5f, draft.vector.x);
        Assert.True(Assert.Single(m_runtime.interactions.documents.documents).isDirty);
        Assert.False(m_runtime.interactions.documents.Save(Assert.Single(m_runtime.interactions.documents.documents).documentId));
    }

    [Fact]
    public void MultipleMaterialSamplesCommitAsOneIndependentUndoWithoutPublishing()
    {
        (AssetPath firstPath, ShaderPropertyId property) = CreateMaterial();
        AssetPath secondPath = AssetPath.Project("SecondSurface.imaterial");
        AssetSourceStore sources = m_assets.CreateSourceStore();
        sources.Save(secondPath, sources.Read(firstPath).bytes, null);
        Assert.True(m_assets.Import(secondPath));
        MaterialDocuments documents = Assert.IsType<MaterialDocuments>(m_sink.materials);
        Guid first = documents.Open(firstPath), second = documents.Open(secondPath);
        byte[] original = sources.Read(firstPath).bytes;
        void Sample(float value)
        {
            var candidates = new Dictionary<Guid, MaterialAsset>();
            foreach (Guid id in new[] { first, second })
            {
                MaterialAsset candidate = documents.Read(id);
                candidate.Set(property, MaterialValue.FromColor(new Inno.Core.Mathematics.Color(value, 1, 1, 1)));
                candidates.Add(id, candidate);
            }
            documents.ReplaceMany(candidates, false);
        }
        Sample(0.2f); Sample(0.4f); documents.CommitMany([first, second]);
        Sample(0.6f); documents.CommitMany([first, second]);
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        foreach (Guid id in new[] { first, second })
        {
            Assert.True(documents.Read(id).TryGet(property, out MaterialValue value));
            Assert.Equal(0.4f, value.vector.x);
        }
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        foreach (Guid id in new[] { first, second }) Assert.False(documents.Read(id).TryGet(property, out _));
        Assert.All(m_runtime.interactions.documents.documents, document => Assert.False(document.isDirty));
        Assert.Equal(original, sources.Read(firstPath).bytes);
        Assert.Equal(original, sources.Read(secondPath).bytes);
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        foreach (Guid id in new[] { first, second }) Assert.True(documents.Read(id).TryGet(property, out _));
    }

    [Fact]
    public void CommonMaterialEditingPreservesUntouchedMixedComponentsAndTextureReferences()
    {
        var first = MaterialValue.FromVector(new Inno.Core.Mathematics.Vector4(1, 2, 3, 4));
        var edited = MaterialValue.FromVector(new Inno.Core.Mathematics.Vector4(8, 2, 3, 4));
        var other = MaterialValue.FromVector(new Inno.Core.Mathematics.Vector4(5, 6, 7, 9));
        MaterialValue result = ShaderPropertyInspector.ApplyEdit(ShaderPropertyType.Vector4, first, edited, other);
        Assert.Equal(new Inno.Core.Mathematics.Vector4(8, 6, 7, 9), result.vector);
        var firstTexture = new TextureAsset(1, 1, TextureColorSpace.Linear, "first");
        var secondTexture = new TextureAsset(1, 1, TextureColorSpace.Linear, "second");
        first = MaterialValue.FromTexture(firstTexture);
        edited = first; edited.sampler = new(RenderSamplerFilter.Point, RenderSamplerAddressMode.Clamp, RenderSamplerAddressMode.Clamp, RenderSamplerAddressMode.Clamp);
        other = MaterialValue.FromTexture(secondTexture);
        result = ShaderPropertyInspector.ApplyEdit(ShaderPropertyType.Texture2D, first, edited, other);
        Assert.Same(secondTexture, result.texture);
        Assert.Equal(RenderSamplerFilter.Point, result.sampler.filter);
    }


    [Fact]
    public void PipelineSettingsDraftsPreserveReferencesAndPublishOnlyAfterSave()
    {
        (AssetPath materialPath, _) = CreateMaterial();
        MaterialAsset material = m_assets.Load<MaterialAsset>(materialPath);
        var originalSettings = new WorkflowPipelineSettings { material = material, exposure = 1 };
        var initial = new RenderPipelineAsset
        {
            pipelineTypeId = "tests.workflow.pipeline",
            pipelineState = new(m_assets.CaptureProperties(originalSettings))
        };
        AssetPath path = AssetPath.Project("Workflow.irenderpipeline");
        AssetSourceStore sources = m_assets.CreateSourceStore();
        sources.Save(path, sources.Encode(initial), null);
        Assert.True(m_assets.Import(path));
        byte[] before = sources.Read(path).bytes;
        RenderPipelineAsset canonical = m_assets.Load<RenderPipelineAsset>(path);
        PipelineDocuments documents = Assert.IsType<PipelineDocuments>(m_sink.pipelines);
        Guid id = documents.Open(path);
        documents.ReplaceSettings(id, new WorkflowPipelineSettings { material = material, exposure = 3 });
        Assert.Equal(before, sources.Read(path).bytes);
        Assert.Equal(1, Read(canonical).exposure);
        Assert.Equal(3, Read(documents.Read(id)).exposure);
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal(1, Read(documents.Read(id)).exposure);
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        RenderPipelineAsset draft = documents.Read(id);
        Assert.Equal(material.identity.persistentId, Assert.Single(draft.pipelineState.dependencies).persistentId);
        EditorDocumentContext document = Assert.Single(m_runtime.interactions.documents.documents);
        Assert.True(m_runtime.interactions.documents.Save(document.documentId));
        Assert.Equal(1, Read(canonical).exposure);
        Tick();
        Assert.Equal(3, Read(canonical).exposure);
        Assert.Same(material, Read(canonical).material);
        Assert.True(m_assets.TryGetInfo(path, out AssetInfo? info));
        Assert.Contains(m_assets.GetDependencies(canonical), value => value.persistentId == material.identity.persistentId);
        documents.ReplaceSettings(id, new WorkflowPipelineSettings { material = null, exposure = 8 });
        Assert.True(m_runtime.interactions.documents.Revert(document.documentId));
        Assert.Same(material, Read(documents.Read(id)).material);
        Assert.Equal(3, Read(documents.Read(id)).exposure);

        WorkflowPipelineSettings Read(RenderPipelineAsset pipeline)
        {
            var result = new WorkflowPipelineSettings();
            m_assets.RestoreProperties(pipeline.pipelineState.stableTypeId, pipeline.pipelineState.propertyData, result);
            return result;
        }
    }

    [Fact]
    public void PipelineMissingSettingsKeepOpaqueBytesThroughHistoryAndRecovery()
    {
        var unknown = new SerializedRenderExtensionState(Guid.NewGuid(), new byte[] { 19, 42, 7 });
        var source = new RenderPipelineAsset { pipelineTypeId = "tests.missing-pipeline", pipelineState = unknown };
        AssetPath path = AssetPath.Project("Missing.irenderpipeline");
        AssetSourceStore sources = m_assets.CreateSourceStore();
        sources.Save(path, sources.Encode(source), null);
        Assert.True(m_assets.Import(path));
        PipelineDocuments documents = Assert.IsType<PipelineDocuments>(m_sink.pipelines);
        Guid id = documents.Open(path);
        RenderPipelineAsset draft = documents.Read(id);
        draft.features = [new("tests.optional", unknown, false)];
        documents.Replace(id, draft);
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Empty(documents.Read(id).features);
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        m_runtime.Dispose();
        m_runtime = CreateRuntime();
        documents = Assert.IsType<PipelineDocuments>(m_sink.pipelines);
        documents.Open(path);
        RenderPipelineAsset recovered = documents.Read(id);
        Assert.Equal(unknown.stableTypeId, recovered.pipelineState.stableTypeId);
        Assert.Equal(unknown.propertyData, recovered.pipelineState.propertyData);
        Assert.Equal(unknown.propertyData, Assert.Single(recovered.features).state.propertyData);
        Assert.Empty(sources.Decode<RenderPipelineAsset>(sources.Read(path).bytes).features);
    }

    [StableTypeId("8c096b6f-92c3-48ed-917e-955858d6a65c")]
    public sealed class WorkflowPipelineSettings : ISerializable
    {
        [SerializableProperty] public MaterialAsset? material { get; set; }
        [SerializableProperty] public float exposure { get; set; }
    }

    private (AssetPath, ShaderPropertyId) CreateMaterial()
    {
        AssetFileEntry shaderEntry = Create("MaterialSurface.ishader");
        ShaderAsset shader = m_assets.Load<ShaderAsset>(shaderEntry.assetPath);
        AssetPath path = AssetPath.Project("Surface.imaterial");
        AssetSourceStore sources = m_assets.CreateSourceStore();
        sources.Save(path, sources.Encode(new MaterialAsset { shader = shader }), null);
        Assert.True(m_assets.Import(path));
        return (path, Assert.Single(shader.definition!.properties).id);
    }

    private GraphDocumentController Controller(AssetFileEntry entry)
    {
        Assert.NotNull(m_sink.graphs);
        Assert.True(m_sink.graphs.TryOpenDocument(AssetId(entry), m_runtime.interactions.history, out GraphDocumentController? controller));
        return controller!;
    }

    private GraphPosition ReadPosition(AssetFileEntry entry, GraphNodeId id) => m_source.Read(entry.assetPath).document.FindNode(id)!.position;
    private Guid AssetId(AssetFileEntry entry)
    {
        Assert.True(m_assets.TryGetInfo(entry.assetPath, out AssetInfo? info));
        return info!.persistentId;
    }
    private void SelectAndDraw(AssetFileEntry entry) { m_runtime.interactions.SetSelection(entry); Draw(); }
    private void Tick() { m_runtime.Update(new(1f / 60, 0, true)); Draw(); }
    private void Draw(Vector2? size = null)
    {
        EditorPanelExtension panel = Assert.Single(m_runtime.panels, panel => panel.id == "rendering.shader-editor");
        panel.isOpen = true;
        UI.NewFrame();
        UI.SetNextWindowPos(new(0, 0));
        UI.SetNextWindowSize(size ?? new(1200, 800));
        _ = UI.Begin("Shader Editor Workflow");
        try
        {
            Assert.True(panel.Draw(m_runtime.context));
            m_canvasMinimum = UI.GetItemRectMin();
            m_canvasMaximum = UI.GetItemRectMax();
        }
        finally { UI.End(); UI.Render(); }
    }

    public void Dispose()
    {
        m_runtime.Dispose();
        UI.DestroyContext(m_imgui);
        m_artifacts.Dispose();
        m_assets.Dispose();
        m_reporter.Dispose();
        m_serialization.Dispose();
        m_types.Dispose();
        m_modules.Dispose();
        m_logs.Dispose();
        m_identityScope.Dispose();
        Directory.Delete(m_root, true);
    }

    public sealed class WorkflowSink
    {
        public GraphEditorModule? graphs { get; set; }
        public MaterialDocuments? materials { get; set; }
        public PipelineDocuments? pipelines { get; set; }
    }

    private sealed class WorkflowLogs : ILogSink
    {
        internal readonly ConcurrentQueue<string> messages = new();
        public void Receive(LogEntry entry) => messages.Enqueue(entry.message);
    }

    [ShaderNodeDrawer("tests.shader-ui-probe")]
    public sealed class CanvasProbeDrawer : ShaderNodeDrawer
    {
        public static Vector2 header;
        public override void Draw(ShaderNodeDrawContext context)
        {
            float zoom = UI.GetWindowSize().X / 246f;
            header = UI.GetWindowPos() + new Vector2(60, -24) * zoom;
            UI.TextUnformatted("Extension controls");
        }
    }
    [EditorModule("tests.shader-workflow", order: 160)]
    public sealed class WorkflowProbe : EditorModule
    {
        public WorkflowProbe(GraphEditorModule graphs, MaterialDocuments materials, PipelineDocuments pipelines, WorkflowSink sink)
        { sink.graphs = graphs; sink.materials = materials; sink.pipelines = pipelines; }
    }

    private sealed class WorkflowCompiler : IShaderCompilerToolchain
    {
        public string implementationId => "tests.workflow";
        public IReadOnlyList<string> supportedSourceLanguages => [];
        public ShaderCompileTarget CreateTarget(GraphicsCapabilities capabilities, bool optimize = true, bool debugInformation = false)
            => new("tests:workflow", capabilities, optimize, debugInformation);
        public ValueTask<ShaderStageToolResult> CompileAsync(ShaderStageToolRequest request, CancellationToken cancellationToken)
            => ValueTask.FromResult(new ShaderStageToolResult([1, 2, 3], request.stage.inputs.Where(input => input.kind == ShaderIrInputKind.Uniform)
                .Select(input => new ShaderStageBinding(input.id, "native_" + input.id, 0)), []));
    }

    private sealed class EmptyPreviews : IEditorPreviewService
    {
        public bool TryRender(EditorViewportComposition composition, out EditorPreviewHandle handle) { handle = default; return false; }
        public void ReleaseRendered(string viewportId) { }
        public uint deviceGeneration => 1;
        public bool TryGetTexture(TextureAsset texture, out EditorPreviewHandle handle) { handle = default; return false; }
        public bool TryGetTextureArtifact(RenderTextureArtifactReference texture, int pixelWidth, int pixelHeight, out EditorPreviewHandle handle) { handle = default; return false; }
        public void Draw(EditorPreviewHandle handle, Vector2 logicalSize) => throw new InvalidOperationException("No texture was requested by this workflow.");
        public bool Release(EditorPreviewHandle handle) => false;
        public void ReleaseAll() { }
    }
}
