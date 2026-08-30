using System;
using System.Collections.Generic;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Assets.Loader;
using Inno.Core.Scripting;

namespace Inno.Assets;

/// <summary>
/// Holds an isolated source-mount candidate that can be inspected before it atomically replaces the active Asset Database.
/// </summary>
public sealed class AssetSourceMountTransaction : IDisposable
{
    private readonly IReadOnlyList<AssetSourceMount> m_mounts;

    internal AssetSourceMountTransaction(
        IReadOnlyList<AssetSourceMount> mounts,
        AssetLoader candidateLoader,
        AssetFileSystem candidateFileSystem,
        string candidateCatalogLibraryRoot)
    {
        m_mounts = mounts;
        this.candidateLoader = candidateLoader;
        this.candidateFileSystem = candidateFileSystem;
        this.candidateCatalogLibraryRoot = candidateCatalogLibraryRoot;
    }

    internal AssetLoader candidateLoader { get; }

    internal AssetFileSystem candidateFileSystem { get; }

    internal string candidateCatalogLibraryRoot { get; }

    internal AssetLoader? previousLoader { get; set; }

    internal AssetFileSystem? previousFileSystem { get; set; }

    internal IReadOnlyList<AssetSourceMount>? previousMounts { get; set; }

    internal AssetManagerOptions previousOptions { get; set; }

    internal bool isActivated { get; set; }

    internal bool isFinished { get; set; }

    /// <summary>Gets the complete isolated mount snapshot represented by this candidate.</summary>
    [ScriptingApiIgnore]
    public IReadOnlyList<AssetSourceMount> sourceMounts => m_mounts;

    /// <summary>Gets candidate source entries without publishing them to active AssetManager consumers.</summary>
    /// <param name="includeDirectories">Whether directory entries should be included.</param>
    /// <returns>A stable candidate entry snapshot.</returns>
    [ScriptingApiIgnore]
    public IReadOnlyList<AssetFileEntry> GetFileSystemEntries(bool includeDirectories = true)
    {
        EnsureOpen();
        return candidateFileSystem.GetEntries(includeDirectories);
    }

    /// <summary>Loads one candidate asset by isolated source path.</summary>
    /// <typeparam name="TAsset">Required asset type.</typeparam>
    /// <param name="path">Candidate source path.</param>
    /// <returns>The candidate asset instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no compatible candidate asset exists.</exception>
    [ScriptingApiIgnore]
    public TAsset Load<TAsset>(AssetPath path) where TAsset : AssetObject
    {
        EnsureOpen();
        using IDisposable resolver = AssetManager.PushReferenceResolver(candidateLoader);
        AssetObject? asset = candidateLoader.Load(path, typeof(TAsset));
        return asset as TAsset ?? throw new InvalidOperationException(
            $"Candidate asset '{path}' cannot be loaded as '{typeof(TAsset).FullName}'.");
    }

    /// <summary>Tries to get candidate catalog information by isolated source path.</summary>
    /// <param name="path">Candidate source path.</param>
    /// <param name="info">Candidate catalog information when available.</param>
    /// <returns>True when the candidate path is cataloged.</returns>
    [ScriptingApiIgnore]
    public bool TryGetInfo(AssetPath path, out AssetInfo? info)
    {
        EnsureOpen();
        return candidateLoader.TryGetInfo(path, out info);
    }

    /// <summary>Tries to resolve one named candidate artifact.</summary>
    /// <param name="persistentId">Candidate asset identity.</param>
    /// <param name="outputName">Named artifact output.</param>
    /// <param name="artifact">Artifact information when available.</param>
    /// <returns>True when the candidate output exists.</returns>
    [ScriptingApiIgnore]
    public bool TryGetArtifact(
        Guid persistentId,
        string outputName,
        out AssetArtifactInfo? artifact)
    {
        EnsureOpen();
        return candidateLoader.TryGetArtifact(persistentId, outputName, out artifact);
    }

    /// <summary>
    /// Activates this candidate without releasing the previous generation or notifying observers.
    /// </summary>
    [ScriptingApiIgnore]
    public void Activate() => AssetManager.ActivatePreparedSourceMounts(this);

    /// <summary>Commits an activated candidate, notifies observers, and retires the previous generation.</summary>
    [ScriptingApiIgnore]
    public void Complete() => AssetManager.CompletePreparedSourceMounts(this);

    /// <summary>Discards the candidate or restores the previous generation after provisional activation.</summary>
    [ScriptingApiIgnore]
    public void Rollback() => AssetManager.RollbackPreparedSourceMounts(this);

    /// <inheritdoc />
    public void Dispose()
    {
        Rollback();
        GC.SuppressFinalize(this);
    }

    private void EnsureOpen()
    {
        if (isFinished)
            throw new ObjectDisposedException(nameof(AssetSourceMountTransaction));
    }
}
