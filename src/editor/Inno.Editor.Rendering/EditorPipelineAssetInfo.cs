using System;
using Inno.Rendering;

namespace Inno.Editor.Rendering;

/// <summary>
/// Describes one project pipeline asset without exposing its reloadable runtime object to editor panels.
/// </summary>
public readonly record struct EditorPipelineAssetInfo
{
    /// <summary>Creates immutable pipeline picker data.</summary>
    /// <param name="assetPath">Project-relative asset path.</param>
    /// <param name="displayName">Artist-facing asset name.</param>
    /// <param name="defaultRenderPath">Default path declared by the asset.</param>
    public EditorPipelineAssetInfo(
        string assetPath,
        string displayName,
        RenderPath defaultRenderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        this.assetPath = assetPath;
        this.displayName = displayName;
        this.defaultRenderPath = defaultRenderPath;
    }

    /// <summary>Gets the project-relative asset path.</summary>
    public string assetPath { get; }

    /// <summary>Gets the artist-facing asset name.</summary>
    public string displayName { get; }

    /// <summary>Gets the default render path declared by the asset.</summary>
    public RenderPath defaultRenderPath { get; }
}
