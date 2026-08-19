using System;

using Inno.Assets.Core;
using Inno.Editor.Core;

namespace Inno.Editor.Assets.AssetEditors;

/// <summary>Provides an immutable snapshot for an asset editor operation.</summary>
public sealed class AssetEditorContext
{
    /// <summary>Creates an asset editor context.</summary>
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
