using System;
using System.Collections.Generic;
using Inno.Core.Graphs;
using Inno.Editor.Core;
using Inno.Editor.Interactions;

namespace Inno.Editor.Graph;

/// <summary>Owns active neutral graph document sessions resolved by reload-safe Editor History.</summary>
[EditorModule("graph.documents", order: 150)]
public sealed class GraphEditorModule : EditorModule
{
    private readonly Dictionary<string, GraphDocumentSession> m_sessions = new(StringComparer.Ordinal);

    /// <summary>Opens or joins an active graph document session.</summary>
    /// <param name="documentId">Stable project-relative asset identity.</param>
    /// <param name="document">Mutable neutral document owned by the current asset generation.</param>
    /// <param name="history">Shared editor history used for every data mutation.</param>
    /// <returns>A presentation-independent document controller.</returns>
    /// <exception cref="ArgumentException">Thrown when the ID is active with a different document instance.</exception>
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

        return new GraphDocumentController(session, history);
    }

    /// <summary>Closes an active graph session and makes its History entries temporarily unavailable.</summary>
    /// <param name="documentId">Stable document identity.</param>
    /// <returns><see langword="true"/> when a session was closed.</returns>
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

    /// <summary>Rebinds an active session to a newly imported current-generation document.</summary>
    /// <param name="documentId">Stable document identity.</param>
    /// <param name="document">New current-generation document instance.</param>
    /// <exception cref="InvalidOperationException">Thrown when the document has no active session.</exception>
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

    /// <inheritdoc />
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
    public GraphDocumentSession(string documentId, GraphDocument document)
    {
        this.documentId = documentId;
        this.document = document;
    }

    public string documentId { get; }
    public GraphDocument document { get; set; }
    public ulong revision { get; set; }
    public bool isDirty { get; set; }
    public bool isOpen { get; set; } = true;
}
