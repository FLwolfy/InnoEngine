using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Identity;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Reload;
using Inno.References;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Interactions;

namespace Inno.Editor.Graph;

/// <summary>
/// Owns active neutral graph document sessions resolved by reload-safe Editor History.
/// </summary>
[EditorModule("graph.documents", order: 150)]
public sealed class GraphEditorModule : EditorModule, IEditorReloadParticipant
{
    private const int C_DOCUMENT_CAPACITY = 128;
    private readonly IdentityAllocator m_identities = new();
    private readonly HashSet<Guid> m_documents = [];
    private readonly List<GraphDocumentSession> m_ownedSessions = [];
    private readonly EditorReloadCoordinator m_reloads;
    private IDisposable? m_registration;
    private readonly SerializationRegistry m_serialization;

    internal GraphEditorModule(SerializationRegistry serialization, EditorReloadCoordinator reloads)
    {
        m_serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
        m_reloads = reloads ?? throw new ArgumentNullException(nameof(reloads));
    }

    internal SerializationRegistry serialization => m_serialization;

    /// <summary>
    /// Opens or joins an active graph document session.
    /// </summary>
    /// <param name="documentId">
    /// Persistent document identity; use the Asset identity for asset-backed graphs.
    /// </param>
    /// <param name="document">
    /// Mutable neutral document owned by the current asset generation.
    /// </param>
    /// <param name="history">
    /// Shared editor history used for every data mutation.
    /// </param>
    /// <returns>
    /// A presentation-independent document controller.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The bounded document owner is full.
    /// </exception>
    public GraphDocumentController OpenDocument(
        Guid documentId,
        GraphDocument document,
        IEditorHistory history)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("A graph document requires a persistent identity.", nameof(documentId));
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(history);
        GraphDocumentSession? session = m_identities.Get<GraphDocumentSession>(documentId);
        if (session is null)
        {
            if (m_documents.Count >= C_DOCUMENT_CAPACITY)
                throw new InvalidOperationException("Graph document capacity has been reached; close unused documents explicitly.");
            session = new GraphDocumentSession(document);
            m_identities.Register(session, documentId);
            m_documents.Add(documentId);
            m_ownedSessions.Add(session);
        }
        // Reattaching presentation joins the existing authoring state, including unsaved Missing nodes.
        return new GraphDocumentController(session, history, m_serialization);
    }

    /// <summary>
    /// Closes a document explicitly; its neutral History entries remain unavailable until the identity is reopened.
    /// </summary>
    /// <param name="documentId">
    /// The persistent document identity to release.
    /// </param>
    /// <returns>
    /// True when a live document was closed.
    /// </returns>
    public bool CloseDocument(Guid documentId)
    {
        GraphDocumentSession? session = m_identities.Get<GraphDocumentSession>(documentId);
        if (session is null) return false;
        m_identities.Unregister(session);
        m_documents.Remove(documentId);
        m_ownedSessions.Remove(session);
        return true;
    }

    /// <summary>
    /// Resolves an already open document without replacing its authored contents.
    /// </summary>
    /// <param name="documentId">
    /// The persistent document identity.
    /// </param>
    /// <param name="history">
    /// The shared history owner for the returned controller.
    /// </param>
    /// <param name="controller">
    /// The joined controller, or null when the document is closed.
    /// </param>
    /// <returns>
    /// True when the document is still owned by this module.
    /// </returns>
    public bool TryOpenDocument(Guid documentId, IEditorHistory history, out GraphDocumentController? controller)
    {
        ArgumentNullException.ThrowIfNull(history);
        GraphDocumentSession? session = m_identities.Get<GraphDocumentSession>(documentId);
        controller = session is null ? null : new GraphDocumentController(session, history, m_serialization);
        return controller is not null;
    }

    /// <summary>
    /// Marks a temporarily Missing source without discarding its graph or advancing its History stack.
    /// </summary>
    /// <param name="documentId">
    /// The persistent identity of an open document.
    /// </param>
    /// <param name="available">
    /// Whether its source can be resolved in the current generation.
    /// </param>
    public void SetAvailability(Guid documentId, bool available)
    {
        GraphDocumentSession? session = m_identities.Get<GraphDocumentSession>(documentId);
        if (session is not null) session.available = available;
    }

    /// <summary>
    /// Rebinds an active session to a newly imported current-generation document.
    /// </summary>
    /// <param name="documentId">
    /// Stable document identity.
    /// </param>
    /// <param name="document">
    /// New current-generation document instance.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the document has no active session.
    /// </exception>
    public void RebindDocument(Guid documentId, GraphDocument document)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("A graph document requires a persistent identity.", nameof(documentId));
        ArgumentNullException.ThrowIfNull(document);
        GraphDocumentSession? session = m_identities.Get<GraphDocumentSession>(documentId);
        if (session is null)
        {
            throw new InvalidOperationException($"Graph document '{documentId}' is not open.");
        }

        if (!session.isDirty)
        {
            session.document = document;
            session.revision++;
        }
        session.available = true;
    }

    internal bool TryResolve(Guid documentId, out GraphDocumentSession? session)
    {
        session = m_identities.Get<GraphDocumentSession>(documentId);
        return session is not null && session.available;
    }

    /// <summary>
    /// Initializes this feature when its owning runtime becomes active.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnStart(EditorContext context) => m_registration = m_reloads.Register(this);

    /// <summary>
    /// Stops this feature before its owning runtime releases the active generation.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnStop(EditorContext context) { m_registration?.Dispose(); m_registration = null; }

    /// <summary>
    /// Unregisters every document identity so issued controllers cannot resolve retired sessions.
    /// </summary>
    protected override void OnDispose()
    {
        foreach (Guid id in m_documents)
        {
            GraphDocumentSession? session = m_identities.Get<GraphDocumentSession>(id);
            if (session is not null) m_identities.Unregister(session);
        }
        m_documents.Clear();
        m_ownedSessions.Clear();
    }

    IGenerationChange IEditorReloadParticipant.Capture(AssemblyReloadContext context)
        => new ReferenceRecoveryTransaction(ReferenceCatalog.empty, [], [new Recovery(this)]);

    void IEditorReloadParticipant.RefreshDiagnostics() { }

    private sealed class Recovery(GraphEditorModule owner) : IReferenceRecoveryParticipant
    {
        private readonly Dictionary<Guid, State> m_previous = [];
        private readonly Dictionary<Guid, GraphDocument> m_candidate = [];

        /// <summary>
        /// Prepares candidate state without changing the active generation.
        /// </summary>
        public void PrepareForActivation()
        {
            foreach (Guid id in owner.m_documents)
            {
                GraphDocumentSession session = owner.m_identities.Get<GraphDocumentSession>(id)!;
                m_previous.Add(id, new State(GraphDocumentCodec.Encode(session.document, owner.m_serialization),
                    session.revision, session.isDirty, session.available));
            }
        }
        /// <summary>
        /// Applies the prepared state at the caller-controlled commit point.
        /// </summary>
        public void Apply()
        {
            foreach ((Guid id, State state) in m_previous)
                m_candidate.Add(id, GraphDocumentCodec.Decode(state.bytes, owner.m_serialization));
            foreach ((Guid id, GraphDocument document) in m_candidate)
                owner.m_identities.Get<GraphDocumentSession>(id)!.document = document;
        }
        /// <summary>
        /// Rejects any recovery that changes captured nodes, edges, properties or order.
        /// </summary>
        /// <param name="changes">
        /// Resolved object-slot changes; graph semantic records are validated from their neutral bytes.
        /// </param>
        public void Validate(IReadOnlyList<ReferenceRecoveryChange> changes)
        {
            foreach ((Guid id, State state) in m_previous)
                if (!state.bytes.AsSpan().SequenceEqual(GraphDocumentCodec.Encode(
                    owner.m_identities.Get<GraphDocumentSession>(id)!.document, owner.m_serialization)))
                    throw new InvalidOperationException("Graph recovery changed neutral authored content.");
        }
        /// <summary>
        /// Completes the committed operation and releases temporary state.
        /// </summary>
        public void Complete() { m_previous.Clear(); m_candidate.Clear(); }
        /// <summary>
        /// Restores the state that existed before candidate activation began.
        /// </summary>
        public void RollbackStructure() => m_candidate.Clear();
        /// <summary>
        /// Restores the state that existed before candidate activation began.
        /// </summary>
        public void RestorePreviousState()
        {
            foreach ((Guid id, State state) in m_previous)
            {
                GraphDocumentSession session = owner.m_identities.Get<GraphDocumentSession>(id)
                    ?? throw new InvalidOperationException("A captured Graph identity disappeared during recovery.");
                session.document = GraphDocumentCodec.Decode(state.bytes, owner.m_serialization);
                session.revision = state.revision;
                session.isDirty = state.dirty;
                session.available = state.available;
            }
            Complete();
        }
        private sealed record State(byte[] bytes, ulong revision, bool dirty, bool available);
    }
}
