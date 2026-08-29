using System;
using System.Collections.Generic;
using Inno.Assets.File;

namespace Inno.Assets.Plugins;

/// <summary>Controls bounded ZIP validation and extraction.</summary>
public sealed class PluginArchiveLimits
{
    /// <summary>Gets default conservative local archive limits.</summary>
    public static PluginArchiveLimits defaults { get; } = new();

    /// <summary>Gets or initializes the maximum number of archive entries.</summary>
    public int maximumEntryCount { get; init; } = 100_000;

    /// <summary>Gets or initializes the maximum uncompressed size of one file.</summary>
    public long maximumFileBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>Gets or initializes the maximum total uncompressed archive size.</summary>
    public long maximumTotalBytes { get; init; } = 1024L * 1024 * 1024;

    /// <summary>Gets or initializes the maximum accepted uncompressed-to-compressed ratio.</summary>
    public double maximumCompressionRatio { get; init; } = 200d;
}

/// <summary>Describes one validated and safely extracted installed Plugin candidate.</summary>
public sealed class PluginArchiveCandidate
{
    internal PluginArchiveCandidate(
        string archivePath,
        string contentHash,
        PluginManifest manifest,
        AssetSourceMount sourceMount,
        bool containsCode,
        bool isTrusted)
    {
        this.archivePath = archivePath;
        this.contentHash = contentHash;
        this.manifest = manifest;
        this.sourceMount = sourceMount;
        this.containsCode = containsCode;
        this.isTrusted = isTrusted;
    }

    /// <summary>Gets the installed ZIP path.</summary>
    public string archivePath { get; }

    /// <summary>Gets the deterministic archive content hash.</summary>
    public string contentHash { get; }

    /// <summary>Gets the validated native manifest.</summary>
    public PluginManifest manifest { get; }

    /// <summary>Gets the extracted read-only asset source mount.</summary>
    public AssetSourceMount sourceMount { get; }

    /// <summary>Gets whether source code exists in this Plugin.</summary>
    public bool containsCode { get; }

    /// <summary>Gets whether code execution was trusted by stable Plugin ID.</summary>
    public bool isTrusted { get; }

    /// <summary>Gets whether content may enter the activation candidate.</summary>
    public bool canActivate => !containsCode || isTrusted;
}

/// <summary>Reports one Plugin discovery, validation, trust, or dependency problem.</summary>
public sealed class PluginArchiveDiagnostic
{
    /// <summary>Creates a Plugin archive diagnostic.</summary>
    /// <param name="archivePath">Related archive path.</param>
    /// <param name="message">Actionable problem description.</param>
    public PluginArchiveDiagnostic(string archivePath, string message)
    {
        this.archivePath = archivePath ?? string.Empty;
        this.message = message ?? string.Empty;
    }

    /// <summary>Gets the related archive path.</summary>
    public string archivePath { get; }

    /// <summary>Gets the actionable problem description.</summary>
    public string message { get; }
}

/// <summary>Contains a complete immutable Plugin discovery result.</summary>
public sealed class PluginScanResult
{
    internal PluginScanResult(
        IReadOnlyList<PluginArchiveCandidate> candidates,
        IReadOnlyList<PluginArchiveDiagnostic> diagnostics)
    {
        this.candidates = candidates;
        this.diagnostics = diagnostics;
    }

    /// <summary>Gets dependency-ordered valid candidates.</summary>
    public IReadOnlyList<PluginArchiveCandidate> candidates { get; }

    /// <summary>Gets isolated diagnostics for rejected candidates.</summary>
    public IReadOnlyList<PluginArchiveDiagnostic> diagnostics { get; }
}
