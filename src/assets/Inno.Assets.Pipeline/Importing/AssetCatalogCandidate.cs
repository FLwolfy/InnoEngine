using System;
using System.IO;
using System.Linq;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Owns the isolated catalog storage used to validate one candidate Asset Pipeline generation.
/// </summary>
/// <remarks>
/// The caller owns <see cref="loader"/> and must dispose it after the candidate has either been
/// committed or discarded. Disposing this object removes only its rebuildable staging storage.
/// </remarks>
public sealed class AssetCatalogCandidate : IDisposable
{
    private readonly string m_activeLibraryRoot;
    private readonly string m_candidateLibraryRoot;
    private bool m_committed;
    private bool m_disposed;

    internal AssetCatalogCandidate(
        string activeLibraryRoot,
        string candidateLibraryRoot,
        AssetLoader loader)
    {
        m_activeLibraryRoot = activeLibraryRoot;
        m_candidateLibraryRoot = candidateLibraryRoot;
        this.loader = loader;
    }

    /// <summary>
    /// Gets the isolated loader whose catalog and in-memory records represent the candidate generation.
    /// </summary>
    public AssetLoader loader { get; }

    /// <summary>
    /// Atomically promotes the validated candidate catalog to the active Library location.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the candidate has already been committed or disposed.
    /// </exception>
    /// <exception cref="IOException">
    /// Thrown when the candidate catalog cannot be promoted to the active Library location.
    /// </exception>
    public void Commit()
    {
        EnsureOpen();
        if (m_committed)
            throw new InvalidOperationException("The Asset catalog candidate has already been committed.");
        loader.PromoteCatalogTo(m_activeLibraryRoot);
        m_committed = true;
    }

    /// <summary>
    /// Removes the candidate catalog staging storage without disposing the candidate loader.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        DeleteCandidateRoot(m_candidateLibraryRoot);
        m_disposed = true;
        GC.SuppressFinalize(this);
    }

    private void EnsureOpen()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
    }

    private static void DeleteCandidateRoot(string candidateLibraryRoot)
    {
        if (!Directory.Exists(candidateLibraryRoot))
            return;
        Directory.Delete(candidateLibraryRoot, recursive: true);
        string? parent = Directory.GetParent(candidateLibraryRoot)?.FullName;
        if (parent is not null && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
            Directory.Delete(parent);
    }
}
