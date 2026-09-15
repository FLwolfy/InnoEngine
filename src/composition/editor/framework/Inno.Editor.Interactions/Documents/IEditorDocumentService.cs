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
    /// Gets the currently owned source documents.
    /// </summary>
    IReadOnlyList<EditorDocumentContext> documents { get; }

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
    /// Opens a document or returns its existing single instance.
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
    EditorDocumentContext Open(string assetPath, Guid assetId = default);

    /// <summary>Updates an open document's source location after an identity-preserving asset move, without changing history or focus.</summary>
    /// <param name="documentId">Existing document identity.</param>
    /// <param name="assetPath">Current authoritative source location for the same persistent asset.</param>
    /// <returns>Whether the document exists.</returns>
    bool UpdateAssetPath(Guid documentId, string assetPath);

    /// <summary>
    /// Updates a document's unsaved state without saving, discarding or changing its History.
    /// </summary>
    /// <param name="documentId">
    /// Stable tab identity.
    /// </param>
    /// <param name="isDirty">Whether the provider's current draft differs from its saved baseline.</param>
    void SetDirty(Guid documentId, bool isDirty = true);

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

}
