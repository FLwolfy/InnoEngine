using System;
using System.Collections.Generic;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Interactions;

namespace Inno.Editor.Graph;

/// <summary>
/// Owns active neutral graph document sessions resolved by reload-safe Editor History.
/// </summary>
[EditorModule("graph.documents", order: 150)]
public sealed class GraphEditorModule : EditorModule
{
    private readonly Dictionary<string, GraphDocumentSession> m_sessions = new(StringComparer.Ordinal);
    private readonly SerializationRegistry m_serialization;

    internal GraphEditorModule(SerializationRegistry serialization)
    {
        m_serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
    }

    internal SerializationRegistry serialization => m_serialization;

    /// <summary>
    /// Opens or joins an active graph document session.
    /// </summary>
    /// <param name="documentId">
    /// Stable project-relative asset identity.
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
    /// <exception cref="ArgumentException">
    /// Thrown when the ID is active with a different document instance.
    /// </exception>
    public GraphDocumentController OpenDocument(
        string documentId,
        GraphDocument document,
        IEditorHistory history)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(history);
        if (!m_sessions.TryGetValue(documentId, out GraphDocumentSession? session))
        {
            session = new GraphDocumentSession(documentId, document);
            m_sessions.Add(documentId, session);
        }
        else if (!ReferenceEquals(session.document, document))
        {
            throw new ArgumentException(
                $"Graph document '{documentId}' is already open with another current-generation instance.",
                nameof(document));
        }

        return new GraphDocumentController(session, history, m_serialization);
    }

    /// <summary>
    /// Closes an active graph session and makes its History entries temporarily unavailable.
    /// </summary>
    /// <param name="documentId">
    /// Stable document identity.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a session was closed.
    /// </returns>
    public bool CloseDocument(string documentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        if (!m_sessions.Remove(documentId, out GraphDocumentSession? session))
        {
            return false;
        }

        session.isOpen = false;
        return true;
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
    public void RebindDocument(string documentId, GraphDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(document);
        if (!m_sessions.TryGetValue(documentId, out GraphDocumentSession? session) || !session.isOpen)
        {
            throw new InvalidOperationException($"Graph document '{documentId}' is not open.");
        }

        session.document = document;
        session.revision++;
        session.isDirty = false;
    }

    internal bool TryResolve(string documentId, out GraphDocumentSession? session)
        => m_sessions.TryGetValue(documentId, out session) && session.isOpen;

    /// <summary>
    /// Releases resources retained by this feature after it has stopped.
    /// </summary>
    protected override void OnDispose()
    {
        foreach (GraphDocumentSession session in m_sessions.Values)
        {
            session.isOpen = false;
        }

        m_sessions.Clear();
    }
}

internal sealed class GraphDocumentSession
{
    /// <summary>
    /// Creates a validated graph document session instance.
    /// </summary>
    /// <param name="documentId">
    /// The document id text validated by the graph document session operation.
    /// </param>
    /// <param name="document">
    /// The document consumed by graph document session; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public GraphDocumentSession(string documentId, GraphDocument document)
    {
        this.documentId = documentId;
        this.document = document;
    }

    /// <summary>
    /// Gets text used for stable identity, presentation, or diagnostics by this contract.
    /// </summary>
    public string documentId { get; }
    /// <summary>
    /// Gets the graph document currently owned by the editor module.
    /// </summary>
    public GraphDocument document { get; set; }
    /// <summary>
    /// Gets the monotonic change identity for the current state.
    /// </summary>
    public ulong revision { get; set; }
    /// <summary>
    /// Gets whether this value is dirty.
    /// </summary>
    public bool isDirty { get; set; }
    /// <summary>
    /// Gets whether this value is open.
    /// </summary>
    public bool isOpen { get; set; } = true;
}
