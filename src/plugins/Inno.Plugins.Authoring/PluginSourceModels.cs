using System;
using System.Collections.Generic;
using Inno.Assets.Pipeline;
using Inno.Plugins;

namespace Inno.Plugins.Authoring;

/// <summary>
/// Identifies the physical container used to install a local Plugin.
/// </summary>
public enum PluginSourceKind
{
    /// <summary>
    /// The Plugin is installed as one source ZIP.
    /// </summary>
    Zip,

    /// <summary>
    /// The Plugin is installed as an unpacked source directory.
    /// </summary>
    Directory,

    /// <summary>
    /// The Plugin is embedded as a complete ZIP inside another installed Plugin package.
    /// </summary>
    EmbeddedZip
}

/// <summary>
/// Controls bounded validation and ZIP extraction for installed Plugin sources.
/// </summary>
public sealed class PluginSourceLimits
{
    /// <summary>
    /// Gets default conservative local Plugin limits.
    /// </summary>
    public static PluginSourceLimits defaults { get; } = new();

    /// <summary>
    /// Gets or initializes the maximum number of source entries.
    /// </summary>
    public int maximumEntryCount { get; init; } = 100_000;

    /// <summary>
    /// Gets or initializes the maximum size of one source file.
    /// </summary>
    public long maximumFileBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>
    /// Gets or initializes the maximum total source size.
    /// </summary>
    public long maximumTotalBytes { get; init; } = 1024L * 1024 * 1024;

    /// <summary>
    /// Gets or initializes the maximum accepted ZIP uncompressed-to-compressed ratio.
    /// </summary>
    public double maximumCompressionRatio { get; init; } = 200d;

    /// <summary>
    /// Gets or initializes the maximum number of embedded dependency packages in one installation.
    /// </summary>
    public int maximumEmbeddedPluginCount { get; init; } = 256;

    /// <summary>
    /// Gets or initializes the maximum nested embedded dependency depth.
    /// </summary>
    public int maximumEmbeddedDepth { get; init; } = 16;
}

/// <summary>
/// Describes one validated installed Plugin source candidate.
/// </summary>
public sealed class PluginCandidate
{
    internal PluginCandidate(
        string sourcePath,
        PluginSourceKind sourceKind,
        string contentHash,
        PluginManifest manifest,
        AssetSourceMount sourceMount,
        bool containsCode)
    {
        this.sourcePath = sourcePath;
        this.sourceKind = sourceKind;
        this.contentHash = contentHash;
        this.manifest = manifest;
        this.sourceMount = sourceMount;
        this.containsCode = containsCode;
    }

    /// <summary>
    /// Gets the installed ZIP or directory path.
    /// </summary>
    public string sourcePath { get; }

    /// <summary>
    /// Gets the physical source container kind.
    /// </summary>
    public PluginSourceKind sourceKind { get; }

    /// <summary>
    /// Gets the deterministic complete source-content hash.
    /// </summary>
    public string contentHash { get; }

    /// <summary>
    /// Gets the validated native manifest.
    /// </summary>
    public PluginManifest manifest { get; }

    /// <summary>
    /// Gets the isolated read-only asset source mount.
    /// </summary>
    public AssetSourceMount sourceMount { get; }

    /// <summary>
    /// Gets whether source code exists in this Plugin.
    /// </summary>
    public bool containsCode { get; }

}

/// <summary>
/// Reports one Plugin discovery, validation, or dependency problem.
/// </summary>
public sealed class PluginDiagnostic
{
    /// <summary>
    /// Creates a Plugin source diagnostic.
    /// </summary>
    /// <param name="sourcePath">
    /// Related ZIP or directory path.
    /// </param>
    /// <param name="message">
    /// Actionable problem description.
    /// </param>
    public PluginDiagnostic(string sourcePath, string message)
    {
        this.sourcePath = sourcePath ?? string.Empty;
        this.message = message ?? string.Empty;
    }

    /// <summary>
    /// Gets the related ZIP or directory path.
    /// </summary>
    public string sourcePath { get; }

    /// <summary>
    /// Gets the actionable problem description.
    /// </summary>
    public string message { get; }
}

/// <summary>
/// Contains a complete immutable Plugin discovery result.
/// </summary>
public sealed class PluginScanResult
{
    internal PluginScanResult(
        IReadOnlyList<PluginCandidate> candidates,
        IReadOnlyList<PluginDiagnostic> diagnostics)
    {
        this.candidates = candidates;
        this.diagnostics = diagnostics;
    }

    /// <summary>
    /// Gets dependency-ordered valid candidates.
    /// </summary>
    public IReadOnlyList<PluginCandidate> candidates { get; }

    /// <summary>
    /// Gets isolated diagnostics for rejected candidates.
    /// </summary>
    public IReadOnlyList<PluginDiagnostic> diagnostics { get; }
}
