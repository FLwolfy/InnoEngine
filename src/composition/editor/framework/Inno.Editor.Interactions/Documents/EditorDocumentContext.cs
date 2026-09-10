using System;
using System.Collections.Generic;
using System.IO;

namespace Inno.Editor.Interactions;

/// <summary>
/// Stores only stable, reload-safe document identity and authoring view parameters owned by the document host.
/// </summary>
public sealed class EditorDocumentContext
{
    private readonly Dictionary<string, string> m_viewParameters = new(StringComparer.Ordinal);

    internal EditorDocumentContext(Guid documentId, Guid assetId, string assetPath, string providerId)
    {
        this.documentId = documentId;
        this.assetId = assetId;
        this.assetPath = assetPath;
        this.providerId = providerId;
        title = Path.GetFileName(assetPath);
    }

    /// <summary>
    /// Gets the stable identity of this open tab.
    /// </summary>
    public Guid documentId { get; }

    /// <summary>
    /// Gets the persistent asset identity, or an empty value before the source has one.
    /// </summary>
    public Guid assetId { get; }

    /// <summary>
    /// Gets the normalized project asset path.
    /// </summary>
    public string assetPath { get; }

    /// <summary>
    /// Gets the stable provider identity used to recover across extension reload.
    /// </summary>
    public string providerId { get; }

    /// <summary>
    /// Gets or sets the author-facing tab title.
    /// </summary>
    public string title { get; set; }

    /// <summary>
    /// Gets whether unsaved source or staged changes exist.
    /// </summary>
    public bool isDirty { get; internal set; }

    /// <summary>
    /// Gets whether a current-generation provider is available.
    /// </summary>
    public bool isProviderAvailable { get; internal set; }

    /// <summary>
    /// Gets or sets the stable active viewport tool identity.
    /// </summary>
    public string activeTool { get; set; } = string.Empty;

    /// <summary>
    /// Gets an immutable snapshot of stable view parameters.
    /// </summary>
    public IReadOnlyDictionary<string, string> viewParameters
        => new Dictionary<string, string>(m_viewParameters, StringComparer.Ordinal);

    /// <summary>
    /// Adds or replaces one stable scalar or JSON-formatted view parameter.
    /// </summary>
    /// <param name="key">
    /// Stable provider-local parameter name.
    /// </param>
    /// <param name="value">
    /// Persistent scalar or JSON-formatted value.
    /// </param>
    public void SetViewParameter(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        m_viewParameters[key] = value;
    }

    /// <summary>
    /// Tries to read one stable view parameter.
    /// </summary>
    /// <param name="key">
    /// Stable provider-local parameter name.
    /// </param>
    /// <param name="value">
    /// Receives the stored value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the parameter exists.
    /// </returns>
    public bool TryGetViewParameter(string key, out string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return m_viewParameters.TryGetValue(key, out value!);
    }

    internal EditorDocumentState CaptureState()
        => new(
            documentId,
            assetId,
            assetPath,
            providerId,
            title,
            isDirty,
            activeTool,
            new Dictionary<string, string>(m_viewParameters, StringComparer.Ordinal));

    internal static EditorDocumentContext Restore(EditorDocumentState state)
    {
        var context = new EditorDocumentContext(
            state.documentId,
            state.assetId,
            state.assetPath,
            state.providerId)
        {
            title = state.title,
            isDirty = state.isDirty,
            activeTool = state.activeTool
        };
        foreach ((string key, string value) in state.viewParameters)
            context.m_viewParameters.Add(key, value);
        return context;
    }
}

/// <summary>
/// Contains one reload-safe open-document snapshot.
/// </summary>
/// <param name="documentId">
/// Stable identity of the open document tab.
/// </param>
/// <param name="assetId">
/// Persistent identity of the backing asset, or an empty value before assignment.
/// </param>
/// <param name="assetPath">
/// Normalized project-relative asset path.
/// </param>
/// <param name="providerId">
/// Stable identity of the document provider that owns the editing behavior.
/// </param>
/// <param name="title">
/// Author-facing title displayed by the document host.
/// </param>
/// <param name="isDirty">
/// Whether the document contains uncommitted authoring changes.
/// </param>
/// <param name="activeTool">
/// Stable identity of the active document tool.
/// </param>
/// <param name="viewParameters">
/// Reload-safe provider-owned view parameters.
/// </param>
public sealed record EditorDocumentState(
    Guid documentId,
    Guid assetId,
    string assetPath,
    string providerId,
    string title,
    bool isDirty,
    string activeTool,
    IReadOnlyDictionary<string, string> viewParameters);
