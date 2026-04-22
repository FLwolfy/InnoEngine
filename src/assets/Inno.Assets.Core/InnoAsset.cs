using System;
using System.IO;
using System.Security.Cryptography;

namespace Inno.Assets.Core;

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
    
    internal void RecomputeHash(Stream inputStream)
    {
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(inputStream);
        sourceHash = Convert.ToHexString(hashBytes);
    }
}