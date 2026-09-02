namespace Inno.Rendering;

/// <summary>
/// Describes the current availability of one target-specific rendering artifact.
/// </summary>
public enum RenderTargetArtifactStatus
{
    /// <summary>
    /// A validated artifact is available for immediate use.
    /// </summary>
    Ready,

    /// <summary>
    /// Artifact production is still running and no usable artifact is available yet.
    /// </summary>
    Pending,

    /// <summary>
    /// The active deployment does not contain the requested artifact.
    /// </summary>
    Unavailable,

    /// <summary>
    /// Artifact production completed unsuccessfully and published a specific diagnostic.
    /// </summary>
    Failed
}
