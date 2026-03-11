using System;
using System.IO;
using System.Security.Cryptography;

using Inno.Assets.Core;
using Inno.Assets.Serializer;

namespace Inno.Assets.AssetType;

public abstract class InnoAsset
{
    [AssetProperty] private string type { get; set; }
    [AssetProperty] public Guid guid { get; internal set; }
    [AssetProperty] internal string sourceHash { get; private set; } = string.Empty;
    [AssetProperty] internal string sourcePath { get; private set; } = string.Empty;

    public string name => Path.GetFileName(sourcePath);
    public byte[] assetBinaries { get; internal set; } = [];
    
    protected InnoAsset()
    {
        type = GetType().Name;
        guid = Guid.NewGuid();
    }
    
    internal void RecomputeHash(string relativePath)
    {
        using var stream = File.OpenRead(Path.Combine(AssetManager.assetDirectory, relativePath));
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(stream);
        sourceHash = Convert.ToHexString(hashBytes);
    }

    internal void RecomputeHash(byte[] bytes)
    {
        var hashBytes = SHA256.HashData(bytes);
        sourceHash = Convert.ToHexString(hashBytes);
    }

    internal void SetSourcePath(string relativePath)
    {
        sourcePath = relativePath;
    }
}