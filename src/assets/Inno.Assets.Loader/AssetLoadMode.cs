using System;

namespace Inno.Assets.Loader;

/// <summary>
/// Controls which asset load sources are allowed.
/// </summary>
[Flags]
public enum AssetLoadMode
{
    /// <summary>
    /// No load source is allowed.
    /// </summary>
    None = 0,

    /// <summary>
    /// Try currently loaded memory cache.
    /// </summary>
    MemoryCache = 1 << 0,

    /// <summary>
    /// Try imported metadata/artifact cache on disk.
    /// </summary>
    DiskCache = 1 << 1,

    /// <summary>
    /// Try importing directly from the source file on disk.
    /// </summary>
    DiskRaw = 1 << 2
}