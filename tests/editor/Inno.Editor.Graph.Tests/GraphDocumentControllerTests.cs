using System;
using System.Collections.Generic;
using System.IO;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Reload;
using Inno.Core.Graphs;
using Inno.Core.Logging;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Graph;
using Inno.Editor.Interactions;
using Xunit;

namespace Inno.Editor.Graph.Tests;

public sealed class GraphDocumentControllerTests : IDisposable
{
    private readonly string m_testRoot = Path.Combine(
        Path.GetTempPath(),
        "InnoEditorGraphTests",
        Guid.NewGuid().ToString("N"));
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;
    private readonly EditorInteractionRuntime m_runtime;
    private readonly LogRouter m_logs = new();
    private readonly EditorReloadCoordinator m_reloads = new();
    private readonly GraphModuleSink m_sink = new();

    public GraphDocumentControllerTests()
    {
        _ = typeof(GraphEditorModule);
        m_modules = new ModuleHost(new ModuleHostOptions
        {
            cacheDirectory = Path.Combine(m_testRoot, "Assemblies")
        });
        m_types = new TypeCatalog(m_modules);
        m_serialization = new SerializationRegistry(m_types);
        m_runtime = new EditorInteractionRuntime(
            new EditorContext(m_testRoot),
            m_types,
            m_logs,
            [m_serialization, m_sink, m_reloads]);
        m_runtime.Start();
    }

    public void Dispose()
    {
        m_runtime.Dispose();
        m_serialization.Dispose();
        m_types.Dispose();
        m_modules.Dispose();
        m_logs.Dispose();
        if (Directory.Exists(m_testRoot))
            Directory.Delete(m_testRoot, recursive: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssemblyPublicationPreservesNeutralGraphAndRollsBackLaterParticipantFailure(bool reject)
    {
        GraphEditorModule module = Assert.IsType<GraphEditorModule>(m_sink.module);
        Guid id = Guid.NewGuid();
        GraphDocumentController controller = module.OpenDocument(id, new GraphDocument(), m_runtime.interactions.history);
        controller.AddNode("missing.extension.node", new GraphPosition(5, 7));
        byte[] before = GraphDocumentCodec.Encode(controller.document, m_serialization);
        ulong revision = controller.revision;
        var rejection = new RejectionParticipant(reject);
        using IDisposable registration = m_reloads.Register(rejection);
        string directory = Path.Combine(AppContext.BaseDirectory, "Modules");
        using (AssemblyReloadSession reload = m_modules.BeginReload([new AssemblyLoadRequest
        {
            moduleName = "GraphRecovery", domain = AssemblyDomain.InnoPlugin, scope = AssemblyScope.Runtime,
            mainAssemblyPath = Path.Combine(directory, "Inno.Extensibility.Modules.TestModule.dll"),
            preloadAssemblyPaths = [Path.Combine(directory, "Reloadable.PrivateDependency.dll")]
        }]))
        {
            if (reject) Assert.Throws<InvalidOperationException>(() => m_reloads.Execute(reload));
            else _ = m_reloads.Execute(reload);
        }
        m_modules.generations.Wait();
        Assert.Equal(before, GraphDocumentCodec.Encode(controller.document, m_serialization));
        Assert.Equal(revision, controller.revision);
        Assert.True(controller.isDirty);
        Assert.Equal(GenerationState.Ready, m_modules.generations.state);
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Empty(controller.document.nodes);
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Single(controller.document.nodes);
    }

    private sealed class RejectionParticipant(bool reject) : IEditorReloadParticipant, IGenerationChange
    {
        public IGenerationChange Capture(AssemblyReloadContext context) => this;
        public void RefreshDiagnostics() { }
        public void PrepareForActivation() { }
        public void Apply() { if (reject) throw new InvalidOperationException("Later recovery participant rejected the candidate."); }
        public void Complete() { }
        public void RollbackStructure() { }
        public void RestorePreviousState() { }
    }

    [Fact]
    public void MissingGraphHistoryRecoversWithoutLosingDirtyStateOrReusingStaleController()
    {
        GraphEditorModule module = Assert.IsType<GraphEditorModule>(m_sink.module);
        IEditorHistory history = m_runtime.interactions.history;
        Guid id = Guid.NewGuid();
        GraphDocumentController controller = module.OpenDocument(id, new GraphDocument(), history);
        controller.AddNode("temporarily.missing.node", default);
        string? undoName = history.undoName;
        module.SetAvailability(id, false);
        Assert.False(history.canUndo);
        _ = history.Undo();
        Assert.Equal(undoName, history.undoName);
        Assert.Single(controller.document.nodes);
        module.SetAvailability(id, true);
        Assert.True(history.canUndo);
        _ = history.Undo();
        Assert.Empty(controller.document.nodes);
        _ = history.Redo();
        Assert.Single(controller.document.nodes);
        module.RebindDocument(id, new GraphDocument());
        Assert.Single(controller.document.nodes);
        Assert.True(controller.isDirty);
        Assert.True(module.TryOpenDocument(id, history, out GraphDocumentController? joined));
        Assert.Same(controller.document, joined!.document);
        Assert.True(module.CloseDocument(id));
        Assert.False(controller.isAvailable);
        _ = module.OpenDocument(id, new GraphDocument(), history);
        Assert.Throws<InvalidOperationException>(() => controller.AddNode("stale", default));
    }

    [Fact]
    public void AddNode_RecordsNeutralHistoryPayload()
    {
        GraphEditorModule module = Assert.IsType<GraphEditorModule>(m_sink.module);
        var history = new RecordingHistory();
        var document = new GraphDocument();
        GraphDocumentController controller = module.OpenDocument(Guid.NewGuid(), document, history);

        GraphNodeId nodeId = controller.AddNode("test.node", new GraphPosition(2f, 3f));
        EditorHistoryChange change = Assert.Single(history.changes);

        Assert.Equal(nodeId, Assert.Single(document.nodes).id);
        Assert.True(change.payload.length > 0);
        change.Dispose();
    }

    [Fact]
    public void CopyPaste_RemapsNodesAndPreservesInternalConnections()
    {
        GraphEditorModule module = Assert.IsType<GraphEditorModule>(m_sink.module);
        var history = new RecordingHistory();
        var document = new GraphDocument();
        GraphDocumentController controller = module.OpenDocument(Guid.NewGuid(), document, history);
        GraphNodeId first = controller.AddNode("test.first", new GraphPosition(1f, 2f));
        GraphNodeId second = controller.AddNode("test.second", new GraphPosition(3f, 4f));
        controller.Connect(
            new GraphEndpoint(first, new GraphPortId("out")),
            new GraphEndpoint(second, new GraphPortId("in")));

        GraphClipboardData clipboard = controller.Copy([first, second]);
        IReadOnlyList<GraphNodeId> pasted = controller.Paste(clipboard, new GraphPosition(10f, 20f));

        Assert.Equal(2, pasted.Count);
        Assert.DoesNotContain(first, pasted);
        Assert.DoesNotContain(second, pasted);
        Assert.Equal(4, document.nodes.Count);
        Assert.Equal(2, document.edges.Count);
        GraphNodeRecord pastedFirst = document.FindNode(pasted[0])!;
        Assert.Equal(new GraphPosition(11f, 22f), pastedFirst.position);
        history.DisposeChanges();
    }

    [Fact]
    public void CanvasZoom_PreservesGraphPointUnderPivot()
    {
        var canvas = new GraphCanvasState();
        canvas.SetViewport(new GraphPosition(10f, 20f), 1f);

        canvas.ZoomAt(2f, 110f, 220f);

        Assert.Equal(2f, canvas.zoom);
        Assert.Equal(new GraphPosition(-90f, -180f), canvas.pan);
    }

    private sealed class RecordingHistory : IEditorHistory
    {
        public List<EditorHistoryChange> changes { get; } = [];
        public bool canUndo => false;
        public bool canRedo => false;
        public bool isFaulted => false;
        public string? undoName => null;
        public string? redoName => null;
        public string? undoUnavailableReason => null;
        public string? redoUnavailableReason => null;
        public string? faultReason => null;
        public long residentBytes => 0;
        public long diskBytes => 0;
        public EditorHistoryTransaction BeginTransaction(string name) => throw new NotSupportedException();
        public EditorHistoryResult Execute(string name, EditorHistoryChange change) => throw new NotSupportedException();
        public void RecordApplied(string name, EditorHistoryChange change)
        {
            _ = name;
            changes.Add(change);
        }
        public EditorHistoryResult Undo() => throw new NotSupportedException();
        public EditorHistoryResult Redo() => throw new NotSupportedException();
        public void DisposeChanges()
        {
            foreach (EditorHistoryChange change in changes)
            {
                change.Dispose();
            }

            changes.Clear();
        }
    }

    private sealed class GraphModuleSink
    {
        internal GraphEditorModule? module { get; set; }
    }

    [EditorModule("tests.graph.module-probe")]
    private sealed class GraphModuleProbe : EditorModule
    {
        private readonly GraphEditorModule m_graphs;
        private readonly GraphModuleSink m_sink;

        private GraphModuleProbe(GraphEditorModule graphs, GraphModuleSink sink)
        {
            m_graphs = graphs;
            m_sink = sink;
        }

        protected override void OnStart(EditorContext context)
        {
            _ = context;
            m_sink.module = m_graphs;
        }
    }
}
