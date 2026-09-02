namespace Inno.Assets.Pipeline;

/// <summary>
/// Defines which side of the asset pipeline an <see cref="AssetPipeline"/> instance serves.
/// </summary>
public enum AssetPipelineMode
{
    /// <summary>
    /// Reconciles writable source files and produces immutable artifacts.
    /// </summary>
    Authoring,

    /// <summary>
    /// Loads a read-only deployed catalog and its content-addressed artifacts without source files.
    /// </summary>
    RuntimeArtifacts
}
