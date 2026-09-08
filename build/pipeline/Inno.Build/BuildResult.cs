using System;
using System.Collections.Generic;

namespace Inno.Build;

/// <summary>
/// Reports the immutable outcome, diagnostics, and optional durable output of one build.
/// </summary>
public sealed class BuildResult
{
    private BuildResult(
        bool succeeded,
        string? outputPath,
        BuildTargetId? target,
        string? contentHash,
        int assetCount,
        int artifactBundleCount,
        int runtimeAssemblyCount,
        int embeddedPluginCount,
        IReadOnlyList<BuildDiagnostic>? diagnostics = null)
    {
        this.succeeded = succeeded;
        this.outputPath = outputPath;
        this.target = target;
        this.contentHash = contentHash;
        this.assetCount = assetCount;
        this.artifactBundleCount = artifactBundleCount;
        this.runtimeAssemblyCount = runtimeAssemblyCount;
        this.embeddedPluginCount = embeddedPluginCount;
        this.diagnostics = diagnostics ?? [];
    }

    /// <summary>
    /// Gets whether the build committed a complete output.
    /// </summary>
    public bool succeeded { get; }

    /// <summary>
    /// Gets the atomically committed output path, or <see langword="null"/> when the build failed.
    /// </summary>
    public string? outputPath { get; }

    /// <summary>
    /// Gets the game target, or <see langword="null"/> for a Plugin package.
    /// </summary>
    public BuildTargetId? target { get; }

    /// <summary>
    /// Gets the deterministic content identity, or <see langword="null"/> when the build failed.
    /// </summary>
    public string? contentHash { get; }

    /// <summary>
    /// Gets the number of deployed runtime assets or packaged source assets.
    /// </summary>
    public int assetCount { get; }

    /// <summary>
    /// Gets the number of content-addressed artifact bundles represented by the output.
    /// </summary>
    public int artifactBundleCount { get; }

    /// <summary>
    /// Gets the number of deployed runtime assemblies.
    /// </summary>
    public int runtimeAssemblyCount { get; }

    /// <summary>
    /// Gets the number of embedded dependency Plugin packages.
    /// </summary>
    public int embeddedPluginCount { get; }

    /// <summary>
    /// Gets structured diagnostics emitted during the build.
    /// </summary>
    public IReadOnlyList<BuildDiagnostic> diagnostics { get; }

    internal static BuildResult Success(
        string outputPath,
        BuildTargetId? target,
        string contentHash,
        int assetCount,
        int artifactBundleCount,
        int runtimeAssemblyCount,
        int embeddedPluginCount,
        IReadOnlyList<BuildDiagnostic>? diagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        return new BuildResult(
            true,
            outputPath,
            target,
            contentHash,
            assetCount,
            artifactBundleCount,
            runtimeAssemblyCount,
            embeddedPluginCount,
            diagnostics);
    }

    internal static BuildResult Failure(
        BuildTargetId? target,
        IReadOnlyList<BuildDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (diagnostics.Count == 0)
            throw new ArgumentException("A failed build requires at least one diagnostic.", nameof(diagnostics));
        return new BuildResult(
            false,
            outputPath: null,
            target,
            contentHash: null,
            assetCount: 0,
            artifactBundleCount: 0,
            runtimeAssemblyCount: 0,
            embeddedPluginCount: 0,
            diagnostics);
    }
}
