using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Graphs;
using Inno.Core.IO;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Graph;
using Inno.Editor.Interactions;
using Inno.Editor.Rendering;
using Inno.Editor.Shaders;
using Inno.Editor.Panel.FileBrowser;
using Inno.Rendering;
using Inno.Extensibility.Types;
using Inno.Rendering.Assets;
using Inno.Rendering.Shaders;

namespace Inno.Editor.Panel.ShaderEditor;

[EditorModule("rendering.shader-documents", order: 170)]
internal sealed partial class ShaderEditorDocuments : EditorModule
{
    internal const string C_PORT_SNAPSHOT = "inno.editor.ports";
    private readonly Dictionary<Guid, Draft> m_drafts = [];
    private readonly Dictionary<Guid, ViewState> m_views = [];
    private readonly HashSet<AssetPath> m_pendingImports = [];
    private long m_sourceRevision = long.MinValue;
    private readonly GraphEditorModule m_graphs;
    private readonly ShaderGraphSourceStore m_sources;
    private Inno.Core.Execution.LifetimeScope? m_lifetime;
    private ShaderNodeCompilerRegistry? m_nodes;
    private ShaderSourceFrontendRegistry? m_frontends;
    internal ShaderNodeDrawerRegistry? drawers;
    internal ShaderGraphTemplateRegistry? templates;
    internal ShaderTargetRegistry? targets;
    private readonly TypeCatalog m_types;
    private readonly EditorShaderCompilation m_compilation;
    internal readonly AssetImportSettingsEdits importSettings;
    internal readonly IEditorPreviewService previews;
    internal readonly AssetPipeline assets;
    internal readonly SerializationRegistry serialization;
    internal readonly EditorInteractions interactions;
    internal ShaderClipboardData? clipboard;

    internal ShaderEditorDocuments(AssetPipeline assets, SerializationRegistry serialization, TypeCatalog types,
        GraphEditorModule graphs, EditorInteractions interactions, EditorShaderCompilation compilation, AssetImportSettingsEdits importSettings,
        IEditorPreviewService previews)
    {
        this.assets = assets;
        this.serialization = serialization;
        this.interactions = interactions;
        m_types = types;
        m_graphs = graphs;
        m_sources = new(assets, serialization);
        m_compilation = compilation;
        this.importSettings = importSettings;
        this.previews = previews;
    }

    internal SerializationContext context => AssetSerializationContext.Create(assets);
    internal ShaderNodeCompilerRegistry nodes => m_nodes ?? throw new InvalidOperationException("Shader documents have not started.");
    internal ShaderSourceFrontendRegistry frontends => m_frontends ?? throw new InvalidOperationException("Shader documents have not started.");
    internal long typeVersion => m_types.current.version;

    internal EditorShaderDraftCompilationSnapshot Preview(Draft draft)
    {
        GraphDocumentController controller = Controller(draft);
        long now = Stopwatch.GetTimestamp();
        if (draft.previewRevision != controller.revision)
        {
            draft.previewRevision = controller.revision;
            draft.previewDue = now + Stopwatch.Frequency * 3 / 10;
        }
        if (now < draft.previewDue)
            return new(EditorShaderCompilationState.Compiling, draft.preview?.artifact is not null, [], draft.preview?.artifact);
        return draft.preview = m_compilation.RequestDraft(draft.id, controller.document, controller.revision, RenderShaderVariant.empty);
    }

    internal void ReleasePreview(Draft draft)
    { m_compilation.ReleaseDraft(draft.id); draft.preview = null; draft.previewRevision = ulong.MaxValue; }

    internal Draft Open(AssetFileEntry entry)
    {
        Guid id = AssetId(entry);
        if (m_drafts.TryGetValue(id, out Draft? existing)) return existing;
        EditorDocumentContext document = interactions.documents.Open(entry.assetPath.ToString(), id, revealHost: false);
        return m_drafts[document.assetId];
    }

    internal GraphDocumentController Controller(Draft draft)
        => m_graphs.TryOpenDocument(draft.id, interactions.history, out GraphDocumentController? controller)
            ? controller! : throw new InvalidOperationException("The Shader graph document is not available.");

    internal Guid AssetId(AssetFileEntry entry)
        => assets.TryGetInfo(entry.assetPath, out AssetInfo? info) && info is not null ? info.persistentId
            : throw new InvalidOperationException("The shader asset identity is not available yet.");

    internal bool TryGet(AssetFileEntry entry, out Draft draft)
    {
        draft = null!;
        return assets.TryGetInfo(entry.assetPath, out AssetInfo? info) && info is not null && m_drafts.TryGetValue(info.persistentId, out draft!);
    }

    internal void RefreshCompilation(Draft draft)
    {
        long now = Stopwatch.GetTimestamp();
        if (now < draft.nextCompilationPoll) return;
        draft.nextCompilationPoll = now + Stopwatch.Frequency / 4;
        draft.diagnostics = [];
        try
        {
            if (!assets.TryGetInfo(draft.id, out AssetInfo? info) || info is null)
            { draft.compilationStatus = "Source unavailable"; return; }
            if (info.status != AssetImportStatus.Imported)
            {
                draft.compilationStatus = "Import " + info.status + " · see diagnostics";
                draft.compilationDiagnostics = string.Join("\n", info.diagnostics);
                return;
            }
            if (!assets.TryLoad(draft.id, out ShaderAsset? shader) || shader is null || shader.isMissing)
            { draft.compilationStatus = "Waiting for import"; return; }
            // Compilation observes only the imported asset, never the unsaved document.
            EditorShaderCompilationSnapshot snapshot = m_compilation.Request(shader, RenderShaderVariant.empty);
            draft.compilationStatus = snapshot.state switch
            {
                EditorShaderCompilationState.Compiling => "Compiling…",
                EditorShaderCompilationState.Succeeded => "Compiled",
                _ => "Compilation failed"
            };
            if (snapshot.usingLastGood) draft.compilationStatus += " · using last-good programs";
            draft.compilationDiagnostics = string.Join("\n", snapshot.diagnostics.Select(static value => value.code + ": " + value.message));
            draft.diagnostics = snapshot.diagnostics.ToArray();
        }
        catch (Exception failure) when ((failure is IOException or InvalidOperationException or ArgumentException or FormatException) && Inno.Core.Execution.RetirementPendingException.Find(failure) is null)
        { draft.compilationStatus = "Compilation unavailable"; draft.compilationDiagnostics = failure.Message; }
    }

    internal void RemoveNodes(Draft draft)
    {
        if (draft.canvas.selectedNodes.Count == 0) return;
        GraphDocumentController controller = Controller(draft);
        controller.ReplaceDocument(ShaderGraphBindings.RemoveNodes(controller.document, draft.canvas.selectedNodes, serialization, context), "Delete Shader Nodes");
        if (draft.activeStage is GraphNodeId stage && controller.document.FindNode(stage) is null) draft.activeStage = null;
        draft.navigation.Cancel();
        draft.dragging = draft.boxSelecting = false;
        draft.dragPreview.Clear();
        draft.canvas.CancelConnection();
    }

    internal void Changed(Draft draft)
    {
        GraphDocumentController controller = Controller(draft);
        draft.observedRevision = controller.revision;
        interactions.documents.SetDirty(draft.documentId, controller.isDirty);
        if (!controller.isDirty)
        {
            string recovery = RecoveryPath(draft.id);
            if (File.Exists(recovery)) File.Delete(recovery);
            return;
        }
        try { PreserveRecovery(draft, controller); draft.error = ""; }
        catch (Exception failure) when ((failure is IOException or UnauthorizedAccessException or InvalidOperationException) && Inno.Core.Execution.RetirementPendingException.Find(failure) is null)
        { draft.error = "Recovery could not be written: " + failure.Message; }
    }

    internal bool Save(Draft draft)
    {
        GraphDocumentController controller = Controller(draft);
        if (!controller.isDirty) return true;
        try
        {
            PreserveRecovery(draft, controller);
            if (!controller.isAvailable) throw new IOException("The shader source is unavailable. Recovery and history are retained until it returns.");
            draft.hash = m_sources.Save(draft.path, CaptureSource(draft, controller.document), draft.hash);
            controller.MarkSaved();
            draft.observedRevision = controller.revision;
            m_pendingImports.Add(draft.path);
            draft.error = "";
            draft.status = "Saved";
            draft.nextCompilationPoll = 0;
            string recovery = RecoveryPath(draft.id);
            if (File.Exists(recovery)) File.Delete(recovery);
            // Import runs at the next editor update, independently of persistence success and watcher timing.
            return true;
        }
        catch (Exception failure) when ((failure is IOException or UnauthorizedAccessException or InvalidOperationException) && Inno.Core.Execution.RetirementPendingException.Find(failure) is null)
        {
            draft.error = failure.Message;
            draft.status = "Not saved · recovery retained";
            return false;
        }
    }

    internal bool Reload(Draft draft)
    {
        try
        {
            ShaderGraphSourceSnapshot source = m_sources.Read(draft.path);
            GraphDocumentController controller = Controller(draft);
            controller.ReplaceDocument(source.document, "Reload Shader From Disk");
            controller.MarkSaved();
            draft.hash = source.contentHash;
            draft.readOnly = source.isReadOnly;
            draft.observedRevision = controller.revision;
            draft.error = "";
            draft.status = "Reloaded · compilation is separate";
            string recovery = RecoveryPath(draft.id);
            if (File.Exists(recovery)) File.Delete(recovery);
            return true;
        }
        catch (Exception failure) when ((failure is IOException or UnauthorizedAccessException or InvalidOperationException) && Inno.Core.Execution.RetirementPendingException.Find(failure) is null)
        { draft.error = failure.Message; return false; }
    }

    /// <inheritdoc />
    protected override void OnStart(EditorContext editor)
    {
        m_lifetime = new();
        try
        {
            m_nodes = m_lifetime.Own(new ShaderNodeCompilerRegistry(m_types));
            m_frontends = m_lifetime.Own(new ShaderSourceFrontendRegistry(m_types));
            drawers = m_lifetime.Own(new ShaderNodeDrawerRegistry(m_types));
            templates = m_lifetime.Own(new ShaderGraphTemplateRegistry(m_types));
            targets = m_lifetime.Own(new ShaderTargetRegistry(m_types));
            _ = m_lifetime.Own(interactions.documents.RegisterProvider(new Provider(this)));
        }
        catch (Exception failure)
        {
            try { m_lifetime.Dispose(); }
            catch (Exception retirement) { throw new AggregateException(failure, retirement); }
            throw;
        }
    }

    /// <inheritdoc />
    protected override void OnUpdate(EditorContext editor)
    {
        foreach (AssetPath path in m_pendingImports.ToArray())
        {
            m_pendingImports.Remove(path);
            try { _ = assets.Import(path); }
            catch (Exception failure) when ((failure is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException) && Inno.Core.Execution.RetirementPendingException.Find(failure) is null)
            {
                foreach (Draft draft in m_drafts.Values.Where(draft => draft.path == path))
                { draft.compilationStatus = "Import failed"; draft.compilationDiagnostics = failure.Message; }
            }
        }
        if (m_sourceRevision != assets.revision)
        {
            SynchronizeSources();
            m_sourceRevision = assets.revision;
        }
        foreach (Draft draft in m_drafts.Values)
        {
            GraphDocumentController controller = Controller(draft);
            if (controller.revision != draft.observedRevision) Changed(draft);
        }
    }

    /// <inheritdoc />
    protected override void Capture(EditorState state)
    {
        foreach (Draft draft in m_drafts.Values) RememberView(draft);
        state.Set("views", m_views.Values.ToArray());
    }

    /// <inheritdoc />
    protected override void Restore(EditorState state)
    {
        m_views.Clear();
        foreach (ViewState view in state.Get("views", Array.Empty<ViewState>()))
            if (view.assetId != Guid.Empty && float.IsFinite(view.x) && float.IsFinite(view.y) && float.IsFinite(view.zoom))
                m_views[view.assetId] = view;
    }

    /// <inheritdoc />
    protected override void OnStop(EditorContext editor)
    {
        // Recovery is not an asset save. Closing the panel, shutdown and reload must not apply a draft.
        foreach (Draft draft in m_drafts.Values)
        {
            if (Controller(draft).isDirty) PreserveRecovery(draft, Controller(draft));
            ReleasePreview(draft);
        }
        m_lifetime?.Dispose();
        m_lifetime = null;
        m_frontends = null;
        drawers = null;
        templates = null;
        targets = null;
        m_nodes = null;
    }

    private void Open(EditorDocumentContext document)
    {
        if (m_drafts.ContainsKey(document.assetId)) return;
        AssetPath path = AssetPath.Parse(document.assetPath);
        string recoveryPath = RecoveryPath(document.assetId);
        RecoveryData? recovery = null;
        if (File.Exists(recoveryPath))
        {
            recovery = serialization.Deserialize<RecoveryData>(File.ReadAllBytes(recoveryPath));
            if (recovery.assetId != document.assetId) throw new InvalidDataException("Shader recovery identity does not match its document.");
        }
        if (assets.TryGetInfo(document.assetId, out AssetInfo? info) && info is not null && info.status != AssetImportStatus.Missing)
        {
            path = info.assetPath;
            interactions.documents.UpdateAssetPath(document.documentId, path.ToString());
        }
        ShaderGraphSourceSnapshot? source = null;
        string missing = "";
        try { source = m_sources.Read(path); }
        catch (Exception failure) when ((recovery is not null && failure is IOException or InvalidOperationException or FormatException) && Inno.Core.Execution.RetirementPendingException.Find(failure) is null)
        { missing = failure.Message; }
        GraphDocumentController controller = m_graphs.OpenDocument(document.assetId, source?.document ?? new GraphDocument(), interactions.history);
        if (recovery is not null && !controller.isDirty)
            controller.ReplaceDocument(GraphDocumentCodec.Decode(recovery.graph, serialization), "Recover Shader Edits");
        var draft = new Draft(document.assetId, document.documentId, path, recovery?.hash ?? source!.contentHash, source?.isReadOnly ?? true)
        { observedRevision = controller.revision };
        if (m_views.TryGetValue(draft.id, out ViewState view)) draft.canvas.SetViewport(new(view.x, view.y), view.zoom);
        else draft.frameRequested = true;
        m_drafts.Add(draft.id, draft);
        if (recovery is not null) Changed(draft);
        if (source is null)
        {
            m_graphs.SetAvailability(draft.id, false);
            draft.error = "Recovery restored · source unavailable: " + missing;
        }
    }

    private void PreserveRecovery(Draft draft, GraphDocumentController controller)
        => AtomicFile.WriteAllBytes(RecoveryPath(draft.id), serialization.Serialize(new RecoveryData
        { assetId = draft.id, path = draft.path.ToString(), hash = draft.hash, graph = GraphDocumentCodec.Encode(CaptureSource(draft, controller.document), serialization) }));

    private GraphDocument CaptureSource(Draft draft, GraphDocument graph)
    {
        GraphDocument snapshot = graph.Clone();
        foreach (GraphNodeRecord node in snapshot.nodes)
            if (draft.portSnapshots.TryGetValue(node.id, out ShaderPortSnapshot[]? ports))
                node.SetValue(C_PORT_SNAPSHOT, ShaderGraphDocument.Encode(ports, serialization, context));
        return snapshot;
    }

    private string RecoveryPath(Guid id) => Path.Combine(assets.libraryRoot, "Editor", "ShaderRecovery", id.ToString("N") + ".inno");

    private void SynchronizeSources()
    {
        if (m_drafts.Count == 0) return;
        Dictionary<Guid, AssetFileEntry> entries = [];
        foreach (AssetFileEntry entry in assets.GetFileSystemEntries(includeDirectories: false))
            if (entry.extension == ".ishader" && assets.TryGetInfo(entry.assetPath, out AssetInfo? info) && info is not null)
                entries.Add(info.persistentId, entry);
        foreach (Draft draft in m_drafts.Values)
        {
            GraphDocumentController controller = Controller(draft);
            if (!entries.TryGetValue(draft.id, out AssetFileEntry? entry))
            {
                m_graphs.SetAvailability(draft.id, false);
                draft.readOnly = true;
                draft.error = "Source missing · graph and undo history retained";
                continue;
            }
            try
            {
                bool wasMissing = !controller.isAvailable;
                m_graphs.SetAvailability(draft.id, true);
                draft.path = entry.assetPath;
                interactions.documents.UpdateAssetPath(draft.documentId, draft.path.ToString());
                ShaderGraphSourceSnapshot source = m_sources.Read(draft.path);
                draft.readOnly = source.isReadOnly;
                if (source.contentHash == draft.hash)
                {
                    if (wasMissing) draft.error = "";
                    continue;
                }
                if (controller.isDirty)
                {
                    PreserveRecovery(draft, controller);
                    draft.error = "Source changed externally · reload from disk or copy these edits to a project shader";
                    continue;
                }
                m_graphs.RebindDocument(draft.id, source.document);
                draft.hash = source.contentHash;
                draft.observedRevision = controller.revision;
                draft.error = "";
                draft.status = "Source updated · compilation is separate";
            }
            catch (Exception failure) when ((failure is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException) && Inno.Core.Execution.RetirementPendingException.Find(failure) is null)
            { draft.error = failure.Message; }
        }
    }

    private void RememberView(Draft draft)
        => m_views[draft.id] = new(draft.id, draft.canvas.pan.x, draft.canvas.pan.y, draft.canvas.zoom);

    private readonly record struct ViewState(Guid assetId, float x, float y, float zoom);

    private sealed class Provider(ShaderEditorDocuments owner) : EditorDocumentProvider
    {
        public override string id => "inno.shader.graph";
        public override bool CanOpen(string assetPath) => assetPath.EndsWith(".ishader", StringComparison.OrdinalIgnoreCase);
        public override void Open(EditorDocumentContext context) => owner.Open(context);
        public override void Draw(EditorDocumentContext context)
        {
            owner.Open(context);
            new ShaderEditorCanvas(owner, owner.m_drafts[context.assetId]).Draw();
        }
        public override bool Save(EditorDocumentContext context) => owner.Save(owner.m_drafts[context.assetId]);
        public override bool Revert(EditorDocumentContext context) => owner.Reload(owner.m_drafts[context.assetId]);
        public override void Close(EditorDocumentContext context)
        {
            // The shared host already applied Save/Discard/Cancel policy. Close must never turn Discard into Save.
            if (!owner.m_drafts.TryGetValue(context.assetId, out Draft? draft)) return;
            string recovery = owner.RecoveryPath(draft.id);
            if (File.Exists(recovery)) File.Delete(recovery);
            owner.RememberView(draft);
            owner.ReleasePreview(draft);
            draft.navigation.Cancel();
            owner.m_graphs.CloseDocument(draft.id);
            owner.m_drafts.Remove(draft.id);
        }
    }

    internal sealed class Draft(Guid id, Guid documentId, AssetPath path, string hash, bool readOnly)
    {
        internal readonly Guid id = id;
        internal readonly Guid documentId = documentId;
        internal AssetPath path = path;
        internal string hash = hash;
        internal bool readOnly = readOnly;
        internal ulong observedRevision;
        internal string error = "";
        internal string status = "Saved";
        internal string compilationStatus = "Waiting for import";
        internal string compilationDiagnostics = "";
        internal ShaderDiagnostic[] diagnostics = [];
        internal bool showDiagnostics;
        internal bool previewEnabled;
        internal ulong previewRevision = ulong.MaxValue;
        internal long previewDue;
        internal EditorShaderDraftCompilationSnapshot? preview;
        internal string diagnosticSource = "";
        internal string[] diagnosticLines = [];
        internal int diagnosticLine;
        internal int diagnosticColumn;
        internal bool revealDiagnosticLine;
        internal string diagnosticReadError = "";
        internal long nextCompilationPoll;
        internal string menuSearch = "";
        internal GraphEndpoint? createFromPort;
        internal GraphEdgeId? selectedEdge;
        internal bool frameRequested;
        internal ShaderCanvasGroup[] groups = [];
        internal readonly HashSet<GraphNodeId> expandedPreviews = [];
        internal Guid settingsSource;
        internal byte[] sourceSettings = [];
        internal string sourceSettingsFingerprint = "";
        internal string sourceSettingsStatus = "";
        internal readonly Dictionary<GraphEndpoint, System.Numerics.Vector2> portPoints = [];
        internal readonly GraphCanvasState canvas = new();
        internal GraphPosition menuPosition;
        internal GraphNodeId? activeStage;
        internal string inspectedPass = "";
        internal GraphNodeId[] inspectedNodes = [];
        internal string valueGesture = Guid.NewGuid().ToString("N");
        internal readonly EditorPlanarNavigation navigation = new();
        internal bool dragging;
        internal bool boxSelecting;
        internal System.Numerics.Vector2 pointerStart;
        internal readonly Dictionary<GraphNodeId, GraphPosition> dragStart = [];
        internal readonly Dictionary<GraphNodeId, GraphPosition> dragPreview = [];
        internal ulong portRevision = ulong.MaxValue;
        internal long assetRevision = long.MinValue;
        internal long typeRevision = long.MinValue;
        internal readonly Dictionary<GraphNodeId, ShaderNodePort[]> ports = [];
        internal readonly Dictionary<GraphNodeId, ShaderPortSnapshot[]> portSnapshots = [];
        internal readonly HashSet<GraphEndpoint> missingPorts = [];
        internal readonly Dictionary<GraphNodeId, string> nodeErrors = [];
    }

    private sealed class RecoveryData : ISerializable
    {
        /// <summary>Gets or sets the persistent asset identity.</summary>
        [SerializableProperty] public Guid assetId { get; set; }
        /// <summary>Gets or sets the diagnostic source path.</summary>
        [SerializableProperty] public string path { get; set; } = "";
        /// <summary>Gets or sets the expected disk source fingerprint.</summary>
        [SerializableProperty] public string hash { get; set; } = "";
        /// <summary>Gets or sets the complete neutral unsaved graph.</summary>
        [SerializableProperty] public byte[] graph { get; set; } = [];
    }
}

internal struct ShaderPortSnapshot
{
    public string id { get; set; }
    public ShaderGraphType type { get; set; }
    public GraphPortDirection direction { get; set; }
    public bool required { get; set; }
}
