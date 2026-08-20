using System;

using Inno.Assets.Core;
using Inno.Editor.Core;

namespace Inno.Editor.Assets.AssetEditors;

/// <summary>Provides an immutable snapshot for an asset editor operation.</summary>
public sealed class AssetEditorContext
{
    /// <summary>
    /// Creates an immutable snapshot used by one asset-editor operation.
    /// </summary>
    /// <param name="editorContext">The shared editor context.</param>
    /// <param name="relativePath">The normalized source-relative path of the entry.</param>
    /// <param name="name">The final source path segment displayed by the Asset Browser.</param>
    /// <param name="isDirectory">Whether the source entry represents a directory.</param>
    /// <param name="info">The committed Asset Catalog snapshot when the entry is tracked.</param>
    /// <param name="assetType">The imported runtime asset type when it can be resolved without loading.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="editorContext"/>, <paramref name="relativePath"/>, or <paramref name="name"/> is <see langword="null"/>.</exception>
    public AssetEditorContext(
        EditorContext editorContext,
        string relativePath,
        string name,
        bool isDirectory,
        AssetInfo? info,
        Type? assetType)
    {
        this.editorContext = editorContext ?? throw new ArgumentNullException(nameof(editorContext));
        this.relativePath = relativePath ?? throw new ArgumentNullException(nameof(relativePath));
        this.name = name ?? throw new ArgumentNullException(nameof(name));
        this.isDirectory = isDirectory;
        this.info = info;
        this.assetType = assetType;
    }

    /// <summary>Gets the shared editor context.</summary>
    public EditorContext editorContext { get; }

    /// <summary>Gets the source-relative path.</summary>
    public string relativePath { get; }

    /// <summary>Gets the final source path segment.</summary>
    public string name { get; }

    /// <summary>Gets whether the source represents a directory.</summary>
    public bool isDirectory { get; }

    /// <summary>Gets the cataloged asset information when available.</summary>
    public AssetInfo? info { get; }

    /// <summary>Gets the resolved imported asset type when available.</summary>
    public Type? assetType { get; }
}
