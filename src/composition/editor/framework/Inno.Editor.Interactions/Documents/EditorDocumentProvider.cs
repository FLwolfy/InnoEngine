using System;

namespace Inno.Editor.Interactions;

/// <summary>
/// Implements one reloadable asset-document kind without owning the document host or persistent tab identity.
/// </summary>
public abstract class EditorDocumentProvider
{
    /// <summary>Gets the globally stable provider identifier.</summary>
    public abstract string id { get; }

    /// <summary>Returns whether this provider can open the supplied project asset path.</summary>
    /// <param name="assetPath">Normalized project asset path.</param>
    /// <returns><see langword="true"/> when the path is supported.</returns>
    public abstract bool CanOpen(string assetPath);

    /// <summary>Initializes transient provider state for an opened or restored document.</summary>
    /// <param name="context">Stable document context owned by the host.</param>
    public virtual void Open(EditorDocumentContext context)
        => ArgumentNullException.ThrowIfNull(context);

    /// <summary>Draws the document body inside the unified host's active content region.</summary>
    /// <param name="context">Stable document context owned by the host.</param>
    public abstract void Draw(EditorDocumentContext context);

    /// <summary>Saves all document changes to its asset source.</summary>
    /// <param name="context">Stable document context owned by the host.</param>
    /// <returns><see langword="true"/> when the source is saved.</returns>
    public abstract bool Save(EditorDocumentContext context);

    /// <summary>Applies staged authoring changes and saves their asset source.</summary>
    /// <param name="context">Stable document context owned by the host.</param>
    /// <returns><see langword="true"/> when staged changes are applied.</returns>
    public virtual bool Apply(EditorDocumentContext context) => Save(context);

    /// <summary>Discards staged authoring changes and reloads the last saved source.</summary>
    /// <param name="context">Stable document context owned by the host.</param>
    /// <returns><see langword="true"/> when the source is restored.</returns>
    public virtual bool Revert(EditorDocumentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return true;
    }

    /// <summary>Releases transient provider state after a document tab closes.</summary>
    /// <param name="context">Stable document context owned by the host.</param>
    public virtual void Close(EditorDocumentContext context)
        => ArgumentNullException.ThrowIfNull(context);
}
