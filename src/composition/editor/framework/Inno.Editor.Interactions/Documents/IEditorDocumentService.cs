using System;
using System.Collections.Generic;

namespace Inno.Editor.Interactions;

/// <summary>
/// Selects how a dirty document responds to a close request.
/// </summary>
public enum EditorDocumentCloseMode
{
    /// <summary>
    /// Keeps the document open.
    /// </summary>
    Cancel,
    /// <summary>
    /// Closes the document after saving.
    /// </summary>
    Save,
    /// <summary>
    /// Closes the document and discards unsaved state.
    /// </summary>
    Discard
}

/// <summary>
/// Owns single-instance editor documents independently from reloadable providers and presentation.
/// </summary>
public interface IEditorDocumentService
{
    /// <summary>
    /// Gets open documents in tab order.
    /// </summary>
    IReadOnlyList<EditorDocumentContext> documents { get; }

    /// <summary>
    /// Gets the focused document, or <see langword="null"/>.
    /// </summary>
    EditorDocumentContext? activeDocument { get; }

    /// <summary>
    /// Registers one current-generation document provider.
    /// </summary>
    /// <param name="provider">
    /// Provider instance to register.
    /// </param>
    /// <returns>
    /// A lease that detaches only this provider instance.
    /// </returns>
    IDisposable RegisterProvider(EditorDocumentProvider provider);

    /// <summary>
    /// Opens and focuses a document or focuses its existing single instance.
    /// </summary>
    /// <param name="assetPath">
    /// Project asset path.
    /// </param>
    /// <param name="assetId">
    /// Persistent asset identity when available.
    /// </param>
    /// <returns>
    /// The stable document context.
    /// </returns>
    /// <param name="revealHost">Whether to reveal the shared host; dedicated panels can retain its document lifetime without opening a second canvas.</param>
    EditorDocumentContext Open(string assetPath, Guid assetId = default, bool revealHost = true);

    /// <summary>
    /// Focuses an open document.
    /// </summary>
    /// <param name="documentId">
    /// Stable tab identity.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the document exists.
    /// </returns>
    bool Focus(Guid documentId);

    /// <summary>Updates an open document's source location after an identity-preserving asset move, without changing history or focus.</summary>
    /// <param name="documentId">Existing document identity.</param>
    /// <param name="assetPath">Current authoritative source location for the same persistent asset.</param>
    /// <returns>Whether the document exists.</returns>
    bool UpdateAssetPath(Guid documentId, string assetPath);

    /// <summary>
    /// Marks a document as containing unsaved changes.
    /// </summary>
    /// <param name="documentId">
    /// Stable tab identity.
    /// </param>
    void MarkDirty(Guid documentId);

    /// <summary>
    /// Saves one open document through its current provider.
    /// </summary>
    /// <param name="documentId">
    /// Stable tab identity.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the document is saved.
    /// </returns>
    bool Save(Guid documentId);

    /// <summary>
    /// Saves every dirty open document.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when every dirty document is saved.
    /// </returns>
    bool SaveAll();

    /// <summary>
    /// Applies staged changes through the current provider.
    /// </summary>
    /// <param name="documentId">
    /// Stable tab identity.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when changes are applied.
    /// </returns>
    bool Apply(Guid documentId);

    /// <summary>
    /// Reverts staged changes through the current provider.
    /// </summary>
    /// <param name="documentId">
    /// Stable tab identity.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when changes are reverted.
    /// </returns>
    bool Revert(Guid documentId);

    /// <summary>
    /// Closes one document using an explicit dirty-document decision.
    /// </summary>
    /// <param name="documentId">
    /// Stable tab identity.
    /// </param>
    /// <param name="mode">
    /// Explicit close behavior.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the document closed.
    /// </returns>
    bool Close(Guid documentId, EditorDocumentCloseMode mode);

    /// <summary>
    /// Draws the active document through its current provider.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a provider drew a document.
    /// </returns>
    bool DrawActive();

    /// <summary>
    /// Captures reload-safe open-document state.
    /// </summary>
    /// <returns>
    /// Stable contexts in tab order.
    /// </returns>
    IReadOnlyList<EditorDocumentState> CaptureState();

    /// <summary>
    /// Restores reload-safe open-document state and focuses a tab.
    /// </summary>
    /// <param name="states">
    /// Complete contexts in tab order.
    /// </param>
    /// <param name="activeDocumentId">
    /// Previously focused tab identity.
    /// </param>
    void RestoreState(IEnumerable<EditorDocumentState> states, Guid? activeDocumentId);
}
