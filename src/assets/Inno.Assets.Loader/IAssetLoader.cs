using System;

using Inno.Assets.Core;

namespace Inno.Assets.Loader;

internal interface IAssetLoader
{
    /// <summary>
    /// Load the asset from disk / raw file.
    /// </summary>
    InnoAsset? Load(string path);
    
    /// <summary>
    /// Load the asset directly from bytes.
    /// </summary>
    InnoAsset LoadRaw(string assetName, Guid assetGuid, byte[] rawBytes);
    
    /// <summary>
    /// Save modifications back to the ORIGINAL source file, and sync meta/bin.
    /// relativePath follows the same convention as Load(): path under AssetManager.assetDirectory.
    /// </summary>
    void SaveSource(string relativePath, InnoAsset asset);
}