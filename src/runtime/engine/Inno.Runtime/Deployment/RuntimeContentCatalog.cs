using System;
using System.IO;
using System.Linq;
using Inno.Core.Serialization;

namespace Inno.Runtime;

/// <summary>
/// Describes the single immutable content pack deployed with a Player build.
/// </summary>
public sealed class RuntimeContentCatalog : ISerializable
{
    /// <summary>
    /// Gets or sets the SHA-256 identity of the complete content pack bytes.
    /// </summary>
    [SerializableProperty]
    public string contentHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the content pack file name relative to the packaged Content directory.
    /// </summary>
    [SerializableProperty]
    public string packFileName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the combined build input snapshot fingerprint.
    /// </summary>
    [SerializableProperty]
    public string snapshotFingerprint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of runtime assets in the enclosed asset catalog.
    /// </summary>
    [SerializableProperty]
    public int assetCount { get; set; }

    /// <summary>
    /// Gets or sets the number of content-addressed artifact bundles in the pack.
    /// </summary>
    [SerializableProperty]
    public int artifactBundleCount { get; set; }

    /// <summary>
    /// Gets or sets the number of runtime assemblies in the pack.
    /// </summary>
    [SerializableProperty]
    public int runtimeAssemblyCount { get; set; }

    /// <summary>
    /// Validates content identity, file naming, snapshot identity, and counts.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Thrown when the catalog cannot identify one complete content-addressed deployment.
    /// </exception>
    public void Validate()
    {
        if (!IsSha256(contentHash) || !IsSha256(snapshotFingerprint))
            throw new InvalidDataException("Runtime content identities must be SHA-256 values.");
        if (!string.Equals(packFileName, $"content-{contentHash}.pack", StringComparison.Ordinal))
            throw new InvalidDataException("Runtime content pack file name does not match its content identity.");
        if (assetCount < 0 || artifactBundleCount < 0 || runtimeAssemblyCount <= 0)
            throw new InvalidDataException("Runtime content catalog contains invalid deployment counts.");
    }

    private static bool IsSha256(string value)
        => value is { Length: 64 } && value.All(static character => Uri.IsHexDigit(character));
}
