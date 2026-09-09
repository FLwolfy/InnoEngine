using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Editor.Interactions;

internal sealed class EditorDocumentService : IEditorDocumentService
{
    private readonly Func<bool> m_showHost;
    private readonly List<EditorDocumentContext> m_documents = [];
    private readonly Dictionary<string, EditorDocumentProvider> m_providers = new(StringComparer.Ordinal);
    private Guid? m_activeDocumentId;

    internal EditorDocumentService(Func<bool> showHost)
        => m_showHost = showHost ?? throw new ArgumentNullException(nameof(showHost));

    public IReadOnlyList<EditorDocumentContext> documents => m_documents.ToArray();

    public EditorDocumentContext? activeDocument
        => m_activeDocumentId is Guid id ? Find(id) : null;

    public IDisposable RegisterProvider(EditorDocumentProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider.id);
        if (!m_providers.TryAdd(provider.id, provider))
            throw new InvalidOperationException($"Editor document provider '{provider.id}' is already registered.");
        foreach (EditorDocumentContext document in m_documents.Where(document => document.providerId == provider.id))
        {
            document.isProviderAvailable = true;
            provider.Open(document);
        }
        return new ProviderLease(this, provider);
    }

    public EditorDocumentContext Open(string assetPath, Guid assetId = default)
    {
        string normalizedPath = NormalizePath(assetPath);
        EditorDocumentContext? existing = m_documents.FirstOrDefault(document =>
            assetId != Guid.Empty && document.assetId == assetId
            || assetId == Guid.Empty && string.Equals(document.assetPath, normalizedPath, StringComparison.Ordinal));
        if (existing is not null)
        {
            m_activeDocumentId = existing.documentId;
            _ = m_showHost();
            return existing;
        }
        EditorDocumentProvider provider = m_providers.Values
            .OrderBy(static candidate => candidate.id, StringComparer.Ordinal)
            .FirstOrDefault(candidate => candidate.CanOpen(normalizedPath))
            ?? throw new InvalidOperationException($"No editor document provider can open '{normalizedPath}'.");
        var context = new EditorDocumentContext(Guid.NewGuid(), assetId, normalizedPath, provider.id)
        {
            isProviderAvailable = true
        };
        m_documents.Add(context);
        try
        {
            provider.Open(context);
        }
        catch
        {
            m_documents.Remove(context);
            throw;
        }
        m_activeDocumentId = context.documentId;
        _ = m_showHost();
        return context;
    }

    public bool Focus(Guid documentId)
    {
        if (Find(documentId) is null)
            return false;
        m_activeDocumentId = documentId;
        _ = m_showHost();
        return true;
    }

    public void MarkDirty(Guid documentId)
        => Get(documentId).isDirty = true;

    public bool Save(Guid documentId)
        => Invoke(documentId, static (provider, context) => provider.Save(context), clearDirty: true);

    public bool SaveAll()
    {
        bool succeeded = true;
        foreach (EditorDocumentContext document in m_documents.ToArray())
        {
            if (document.isDirty && !Save(document.documentId))
                succeeded = false;
        }
        return succeeded;
    }

    public bool Apply(Guid documentId)
        => Invoke(documentId, static (provider, context) => provider.Apply(context), clearDirty: true);

    public bool Revert(Guid documentId)
        => Invoke(documentId, static (provider, context) => provider.Revert(context), clearDirty: true);

    public bool Close(Guid documentId, EditorDocumentCloseMode mode)
    {
        EditorDocumentContext? document = Find(documentId);
        if (document is null || mode == EditorDocumentCloseMode.Cancel)
            return false;
        if (document.isDirty && mode == EditorDocumentCloseMode.Save && !Save(documentId))
            return false;
        if (document.isDirty && mode != EditorDocumentCloseMode.Discard && mode != EditorDocumentCloseMode.Save)
            return false;
        if (m_providers.TryGetValue(document.providerId, out EditorDocumentProvider? provider))
            provider.Close(document);
        int index = m_documents.IndexOf(document);
        m_documents.RemoveAt(index);
        if (m_activeDocumentId == documentId)
        {
            m_activeDocumentId = m_documents.Count == 0
                ? null
                : m_documents[Math.Min(index, m_documents.Count - 1)].documentId;
        }
        return true;
    }

    public bool DrawActive()
    {
        EditorDocumentContext? document = activeDocument;
        if (document is null || !m_providers.TryGetValue(document.providerId, out EditorDocumentProvider? provider))
            return false;
        provider.Draw(document);
        return true;
    }

    public IReadOnlyList<EditorDocumentState> CaptureState()
        => m_documents.Select(static document => document.CaptureState()).ToArray();

    public void RestoreState(IEnumerable<EditorDocumentState> states, Guid? activeDocumentId)
    {
        ArgumentNullException.ThrowIfNull(states);
        if (m_documents.Count != 0)
            throw new InvalidOperationException("Document state can only be restored into an empty host.");
        foreach (EditorDocumentState state in states)
        {
            if (state is null
                || state.documentId == Guid.Empty
                || string.IsNullOrWhiteSpace(state.assetPath)
                || string.IsNullOrWhiteSpace(state.providerId)
                || m_documents.Any(document => document.documentId == state.documentId))
            {
                throw new ArgumentException("Document state contains an invalid or duplicate identity.", nameof(states));
            }
            EditorDocumentContext document = EditorDocumentContext.Restore(state);
            if (m_providers.TryGetValue(document.providerId, out EditorDocumentProvider? provider))
            {
                document.isProviderAvailable = true;
                provider.Open(document);
            }
            m_documents.Add(document);
        }
        m_activeDocumentId = activeDocumentId is Guid requested && Find(requested) is not null
            ? requested
            : m_documents.FirstOrDefault()?.documentId;
    }

    internal void Shutdown()
    {
        foreach (EditorDocumentContext document in m_documents.ToArray())
        {
            if (m_providers.TryGetValue(document.providerId, out EditorDocumentProvider? provider))
                provider.Close(document);
        }
        m_documents.Clear();
        m_providers.Clear();
        m_activeDocumentId = null;
    }

    private bool Invoke(
        Guid documentId,
        Func<EditorDocumentProvider, EditorDocumentContext, bool> operation,
        bool clearDirty)
    {
        EditorDocumentContext document = Get(documentId);
        if (!m_providers.TryGetValue(document.providerId, out EditorDocumentProvider? provider))
            return false;
        bool succeeded = operation(provider, document);
        if (succeeded && clearDirty)
            document.isDirty = false;
        return succeeded;
    }

    private EditorDocumentContext Get(Guid documentId)
        => Find(documentId)
           ?? throw new ArgumentException($"Editor document '{documentId}' is not open.", nameof(documentId));

    private EditorDocumentContext? Find(Guid documentId)
        => m_documents.FirstOrDefault(document => document.documentId == documentId);

    private static string NormalizePath(string assetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        string normalized = assetPath.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized;
    }

    private void Unregister(EditorDocumentProvider provider)
    {
        if (!m_providers.TryGetValue(provider.id, out EditorDocumentProvider? registered)
            || !ReferenceEquals(provider, registered))
        {
            return;
        }
        m_providers.Remove(provider.id);
        foreach (EditorDocumentContext document in m_documents.Where(document => document.providerId == provider.id))
            document.isProviderAvailable = false;
    }

    private sealed class ProviderLease(EditorDocumentService owner, EditorDocumentProvider provider) : IDisposable
    {
        private EditorDocumentService? m_owner = owner;

        public void Dispose()
        {
            EditorDocumentService? current = m_owner;
            m_owner = null;
            current?.Unregister(provider);
        }
    }
}
