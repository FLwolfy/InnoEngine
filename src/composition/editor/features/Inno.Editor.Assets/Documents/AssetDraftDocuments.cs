using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.IO;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Interactions;

namespace Inno.Editor.Assets;

/// <summary>Owns detached native asset drafts with shared document, History, recovery and explicit-save semantics.</summary>
/// <typeparam name="TAsset">Native source type edited by the consuming feature.</typeparam>
public sealed class AssetDraftDocuments<TAsset> : IDisposable where TAsset : AssetObject
{
    private readonly Dictionary<Guid, Draft> m_drafts = [];
    private readonly HashSet<AssetPath> m_imports = [];
    private readonly SerializationRegistry m_serialization;
    private readonly AssetSourceStore m_sources;
    private readonly AssetPipeline m_assets;
    private readonly EditorInteractions m_interactions;
    private readonly string m_providerId;
    private readonly string m_historyKind;
    private readonly string m_extension;
    private readonly string m_label;
    private IDisposable? m_provider;
    private long m_sourceRevision = -1;
    private long m_updateSerial;
    private Guid[] m_activeGroup = [];

    /// <summary>Creates a feature-owned draft store; call Start after document services become available.</summary>
    /// <param name="assets">Owner of source identities, serialization and import.</param>
    /// <param name="serialization">Host registry for neutral History and recovery records.</param>
    /// <param name="interactions">Shared documents and History services.</param>
    /// <param name="providerId">Stable unique document provider identifier.</param>
    /// <param name="historyKind">Stable protocol handled by the feature's registered History handler.</param>
    /// <param name="extension">Native source extension including its leading dot.</param>
    /// <param name="label">Human-readable singular asset kind.</param>
    public AssetDraftDocuments(AssetPipeline assets, SerializationRegistry serialization, EditorInteractions interactions,
        string providerId, string historyKind, string extension, string label)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(interactions);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (string.IsNullOrWhiteSpace(extension) || !extension.StartsWith('.') || extension.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException("A native file extension is required.", nameof(extension));
        if (providerId is "." or ".." || providerId.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new ArgumentException("The provider identifier must be a safe single directory name.", nameof(providerId));
        m_assets = assets; m_interactions = interactions; m_serialization = serialization;
        m_sources = m_assets.CreateSourceStore(); m_providerId = providerId; m_historyKind = historyKind;
        m_extension = extension; m_label = label;
    }

    /// <summary>Gets host-owned neutral presentation state for an open document.</summary>
    /// <param name="assetId">Persistent source identity.</param>
    /// <returns>Read-only presentation state; it contains no live asset or extension objects.</returns>
    public Draft GetDraft(Guid assetId) => m_drafts[assetId];

    /// <summary>Marks a document as inspected in this frame so a lost gesture can be completed.</summary>
    /// <param name="assetId">Persistent source identity.</param>
    public void TouchInspection(Guid assetId) => m_drafts[assetId].lastInspection = m_updateSerial;

    /// <summary>Opens a native asset source in the shared document service without revealing a second Inspector.</summary>
    /// <param name="path">Source path, including sources with failed imports.</param>
    /// <returns>The persistent asset identity used for subsequent draft operations.</returns>
    public Guid Open(AssetPath path)
    {
        if (!path.localPath.EndsWith(m_extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The source extension does not belong to this document provider.", nameof(path));
        if (!m_assets.TryGetInfo(path, out AssetInfo? info) || info is null) throw new IOException("The asset identity is unavailable.");
        m_interactions.documents.Open(path.ToString(), info.persistentId, revealHost: false);
        return info.persistentId;
    }

    /// <summary>Reads a detached current-generation value; callers must never mutate its referenced canonical assets.</summary>
    /// <param name="assetId">Open asset identity.</param>
    /// <returns>A detached editable asset whose changes have not been published.</returns>
    public TAsset Read(Guid assetId) => m_sources.Decode<TAsset>(m_drafts[assetId].bytes);

    /// <summary>Updates the open draft through shared History without saving or publishing a runtime asset.</summary>
    /// <param name="assetId">Open asset identity.</param>
    /// <param name="candidate">Detached edited asset.</param>
    /// <param name="finishGesture">True completes one history transaction; false continues the same active gesture.</param>
    public void Replace(Guid assetId, TAsset candidate, bool finishGesture = true)
        => Edit(m_drafts[assetId], candidate, finishGesture);

    /// <summary>Finishes the active edit gesture without saving or applying its draft.</summary>
    /// <param name="assetId">Open asset identity.</param>
    public void Commit(Guid assetId) => Commit(m_drafts[assetId]);

    /// <summary>Edits compatible selected drafts as one gesture without saving any source.</summary>
    /// <param name="candidates">Detached candidate assets indexed by their open asset identities.</param>
    /// <param name="finishGesture">Whether this sample completes the shared gesture.</param>
    public void ReplaceMany(IReadOnlyDictionary<Guid, TAsset> candidates, bool finishGesture = true)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (m_activeGroup.Length != 0 && !m_activeGroup.ToHashSet().SetEquals(candidates.Keys)) CommitMany(m_activeGroup);
        var encoded = new Dictionary<Guid, byte[]>();
        foreach (var pair in candidates)
        {
            Draft draft = m_drafts[pair.Key];
            if (draft.readOnly) throw new InvalidOperationException("The selection contains a read-only asset.");
            encoded.Add(pair.Key, m_sources.Encode(pair.Value));
        }
        foreach (var pair in encoded)
        {
            Draft draft = m_drafts[pair.Key];
            if (draft.bytes.AsSpan().SequenceEqual(pair.Value)) continue;
            draft.gestureBefore ??= draft.bytes;
            draft.bytes = pair.Value;
            Changed(draft);
        }
        m_activeGroup = candidates.Keys.ToArray();
        if (finishGesture) CommitMany(candidates.Keys);
    }

    /// <summary>Commits all selected draft samples in a single shared history transaction.</summary>
    /// <param name="assetIds">Open asset identities belonging to this gesture.</param>
    public void CommitMany(IEnumerable<Guid> assetIds)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        using var transaction = m_interactions.history.BeginTransaction("Edit " + m_label + " Selection");
        foreach (Guid id in assetIds) Commit(m_drafts[id]);
        transaction.Commit();
        m_activeGroup = [];
    }

    private void Edit(Draft draft, TAsset candidate, bool finishGesture)
    {
        if (draft.readOnly) throw new InvalidOperationException("Copy this installed asset to the project before editing.");
        byte[] next = m_sources.Encode(candidate);
        if (!draft.bytes.AsSpan().SequenceEqual(next))
        {
            draft.gestureBefore ??= draft.bytes;
            draft.bytes = next;
            Changed(draft);
        }
        if (finishGesture) Commit(draft);
    }

    private void Commit(Draft draft)
    {
        byte[]? before = draft.gestureBefore;
        if (before is null) return;
        draft.gestureBefore = null;
        if (before.AsSpan().SequenceEqual(draft.bytes)) return;
        var payload = new ChangeData { assetId = draft.id, before = before, after = draft.bytes };
        var change = new EditorHistoryChange(m_historyKind, EditorHistoryPayload.FromBytes(m_serialization.Serialize(payload)));
        try { m_interactions.history.RecordApplied("Edit " + m_label, change); }
        catch
        {
            change.Dispose();
            draft.bytes = before;
            Changed(draft);
            throw;
        }
    }

    /// <summary>Validates a feature's neutral History record without changing the draft.</summary>
    /// <param name="change">Record owned by the shared History service.</param>
    /// <param name="direction">Requested traversal direction.</param>
    public void ValidateHistory(EditorHistoryChange change, EditorHistoryDirection direction)
    { _ = ResolveHistory(change, direction); }

    /// <summary>Applies a previously validated neutral record; failed recovery leaves the draft unchanged.</summary>
    /// <param name="change">Record owned by the shared History service.</param>
    /// <param name="direction">Requested traversal direction.</param>
    public void ApplyHistory(EditorHistoryChange change, EditorHistoryDirection direction)
    {
        (Draft draft, ChangeData data) = ResolveHistory(change, direction);
        draft.bytes = direction == EditorHistoryDirection.Undo ? data.before : data.after;
        Changed(draft);
    }

    private (Draft, ChangeData) ResolveHistory(EditorHistoryChange change, EditorHistoryDirection direction)
    {
        ChangeData data = m_serialization.Deserialize<ChangeData>(change.payload.ReadBytes());
        if (!m_drafts.TryGetValue(data.assetId, out Draft? draft)) throw new InvalidOperationException("Reopen the asset document to use its history.");
        if (draft.gestureBefore is not null) throw new InvalidOperationException("Finish the active asset gesture first.");
        byte[] expected = direction == EditorHistoryDirection.Undo ? data.after : data.before;
        if (!draft.bytes.AsSpan().SequenceEqual(expected)) throw new InvalidOperationException("The asset draft changed outside this history entry.");
        return (draft, data);
    }

    private void Changed(Draft draft)
    {
        m_interactions.documents.SetDirty(draft.documentId, draft.isDirty);
        try
        {
            if (!draft.isDirty) { DeleteRecovery(draft.id); return; }
            AtomicFile.WriteAllBytes(RecoveryPath(draft.id), m_serialization.Serialize(new Recovery
            { assetId = draft.id, hash = draft.hash, baseline = draft.baseline, bytes = draft.bytes }));
            if (draft.error.StartsWith("Recovery write failed:", StringComparison.Ordinal)) draft.error = "";
        }
        catch (Exception error) when (Recoverable(error))
        {
            draft.error = "Recovery write failed: " + error.Message + " · The current draft and Undo history remain in memory. Save or retry before closing the editor.";
        }
    }

    private bool Save(Draft draft)
    {
        try
        {
            Commit(draft);
            RefreshPath(draft);
            draft.hash = m_sources.Save(draft.path, draft.bytes, draft.hash);
            draft.baseline = draft.bytes;
            draft.error = "";
            m_imports.Add(draft.path);
            DeleteRecovery(draft.id);
            return true;
        }
        catch (Exception error) when (Recoverable(error)) { draft.error = error.Message; return false; }
    }

    private bool Revert(Draft draft)
    {
        try
        {
            Commit(draft);
            RefreshPath(draft);
            AssetSourceSnapshot source = m_sources.Read(draft.path);
            byte[] before = draft.bytes;
            draft.gestureBefore = before;
            draft.bytes = source.bytes;
            Commit(draft);
            draft.hash = source.contentHash;
            draft.baseline = draft.bytes;
            draft.readOnly = source.isReadOnly;
            draft.error = "";
            DeleteRecovery(draft.id);
            return true;
        }
        catch (Exception error) when (Recoverable(error)) { draft.error = error.Message; return false; }
    }

    private void RefreshPath(Draft draft)
    {
        if (!m_assets.TryGetInfo(draft.id, out AssetInfo? info) || info is null || info.status == AssetImportStatus.Missing)
            throw new IOException("Asset source is missing. Draft and history are retained.");
        draft.path = info.assetPath;
        m_interactions.documents.UpdateAssetPath(draft.documentId, draft.path.ToString());
    }

    private void Open(EditorDocumentContext document)
    {
        if (m_drafts.ContainsKey(document.assetId)) return;
        AssetPath path = AssetPath.Parse(document.assetPath);
        if (m_assets.TryGetInfo(document.assetId, out AssetInfo? info) && info is not null) path = info.assetPath;
        Recovery? recovery = File.Exists(RecoveryPath(document.assetId))
            ? m_serialization.Deserialize<Recovery>(File.ReadAllBytes(RecoveryPath(document.assetId))) : null;
        if (recovery is not null && recovery.assetId != document.assetId) throw new InvalidDataException("Asset recovery identity does not match its document.");
        AssetSourceSnapshot? source = null;
        try { source = m_sources.Read(path); }
        catch (Exception error) when (recovery is not null && Recoverable(error)) { }
        var draft = new Draft(document.assetId, document.documentId, path, recovery?.bytes ?? source!.bytes,
            recovery?.baseline ?? source!.bytes, recovery?.hash ?? source!.contentHash, source?.isReadOnly ?? true);
        if (recovery is not null && source?.contentHash != recovery.hash) draft.error = "Source changed externally. Recovery retained; resolve or Revert explicitly.";
        m_drafts.Add(draft.id, draft);
        if (draft.isDirty) m_interactions.documents.SetDirty(draft.documentId);
    }

    /// <summary>Registers the feature provider with the shared document host.</summary>
    public void Start()
    {
        if (m_provider is not null) throw new InvalidOperationException("This draft provider is already started.");
        m_provider = m_interactions.documents.RegisterProvider(new Provider(this));
    }

    /// <summary>Completes abandoned gestures, imports explicit saves and reconciles source changes.</summary>
    public void Update()
    {
        m_updateSerial++;
        if (m_activeGroup.Any(id => m_drafts[id].lastInspection >= 0 && m_drafts[id].lastInspection < m_updateSerial - 1))
            CommitMany(m_activeGroup);
        foreach (Draft draft in m_drafts.Values)
            if (draft.gestureBefore is not null && draft.lastInspection >= 0 && draft.lastInspection < m_updateSerial - 1)
                Commit(draft);
        foreach (AssetPath path in m_imports.ToArray())
        {
            m_imports.Remove(path);
            try
            {
                if (!m_assets.Import(path))
                {
                    string diagnostics = m_assets.TryGetInfo(path, out AssetInfo? info) && info is not null
                        ? string.Join("\n", info.diagnostics) : "The asset importer did not publish this asset.";
                    foreach (Draft draft in m_drafts.Values.Where(value => value.path == path))
                        draft.error = "Saved; import failed: " + diagnostics;
                }
            }
            catch (Exception error) when (Recoverable(error))
            { foreach (Draft draft in m_drafts.Values.Where(value => value.path == path)) draft.error = "Saved; import failed: " + error.Message; }
        }
        if (m_sourceRevision != m_assets.revision)
        {
            foreach (Draft draft in m_drafts.Values) SynchronizeSource(draft);
            m_sourceRevision = m_assets.revision;
        }
    }

    private void SynchronizeSource(Draft draft)
    {
        try
        {
            RefreshPath(draft);
            AssetSourceSnapshot source = m_sources.Read(draft.path);
            draft.readOnly = source.isReadOnly;
            if (draft.hash == source.contentHash) return;
            if (draft.isDirty || draft.gestureBefore is not null)
            {
                draft.error = "Source changed externally. Your draft is retained; Revert reloads it without overwriting the external edit.";
                return;
            }
            draft.bytes = source.bytes;
            draft.baseline = source.bytes;
            draft.hash = source.contentHash;
            draft.error = "";
            Changed(draft);
        }
        catch (Exception error) when (Recoverable(error)) { draft.error = error.Message; }
    }

    /// <summary>Preserves unsaved recovery and unregisters the current feature provider.</summary>
    public void Dispose()
    {
        CommitMany(m_activeGroup);
        foreach (Draft draft in m_drafts.Values) { Commit(draft); if (draft.isDirty) Changed(draft); }
        m_provider?.Dispose();
        m_provider = null;
    }

    private string RecoveryPath(Guid id) => Path.Combine(m_assets.libraryRoot, "Editor", "AssetDrafts", m_providerId, id.ToString("N") + ".inno");
    private void DeleteRecovery(Guid id) { string path = RecoveryPath(id); if (File.Exists(path)) File.Delete(path); }
    private static bool Recoverable(Exception error) => error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or FormatException
        && Inno.Core.Execution.RetirementPendingException.Find(error) is null;

    private sealed class Provider(AssetDraftDocuments<TAsset> owner) : EditorDocumentProvider
    {
        public override string id => owner.m_providerId;
        public override bool CanOpen(string assetPath) => assetPath.EndsWith(owner.m_extension, StringComparison.OrdinalIgnoreCase);
        public override void Open(EditorDocumentContext context) => owner.Open(context);
        public override void Draw(EditorDocumentContext context)
            => Inno.Native.ImGui.ImGui.TextUnformatted("Select this asset in the Asset Browser to edit its draft in the Inspector.");
        public override bool Save(EditorDocumentContext context) => owner.Save(owner.m_drafts[context.assetId]);
        public override bool Revert(EditorDocumentContext context) => owner.Revert(owner.m_drafts[context.assetId]);
        public override void Close(EditorDocumentContext context)
        {
            owner.CommitMany(owner.m_activeGroup);
            owner.DeleteRecovery(context.assetId);
            owner.m_drafts.Remove(context.assetId);
        }
    }

    /// <summary>Contains neutral draft status; only the owning store can change it.</summary>
    public sealed class Draft
    {
        internal byte[] bytes;
        internal byte[] baseline;
        internal string hash;
        internal byte[]? gestureBefore;
        internal long lastInspection = -1;
        internal Draft(Guid id, Guid documentId, AssetPath path, byte[] bytes, byte[] baseline, string hash, bool readOnly)
        { this.id = id; this.documentId = documentId; this.path = path; this.bytes = bytes; this.baseline = baseline; this.hash = hash; this.readOnly = readOnly; }
        /// <summary>Gets the persistent source identity.</summary>
        public Guid id { get; }
        /// <summary>Gets the shared document identity used for Save/Revert and close confirmation.</summary>
        public Guid documentId { get; }
        /// <summary>Gets the current resolved source path.</summary>
        public AssetPath path { get; internal set; }
        /// <summary>Gets whether this source is installed read-only content.</summary>
        public bool readOnly { get; internal set; }
        /// <summary>Gets the latest source, save or import diagnostic.</summary>
        public string error { get; internal set; } = "";
        /// <summary>Gets whether detached bytes differ from the saved baseline.</summary>
        public bool isDirty => !bytes.AsSpan().SequenceEqual(baseline);
    }

    private sealed class Recovery : ISerializable
    {
        [SerializableProperty] internal Guid assetId { get; set; }
        [SerializableProperty] internal string hash { get; set; } = "";
        [SerializableProperty] internal byte[] baseline { get; set; } = [];
        [SerializableProperty] internal byte[] bytes { get; set; } = [];
    }

    private sealed class ChangeData : ISerializable
    {
        [SerializableProperty] internal Guid assetId { get; set; }
        [SerializableProperty] internal byte[] before { get; set; } = [];
        [SerializableProperty] internal byte[] after { get; set; } = [];
    }
}
