namespace Inno.Rendering;

/// <summary>
/// Provides contextual data for render feature pass injection.
/// </summary>
public sealed class RenderFeatureContext
{
    public required PipelineFeatureSet features { get; init; }
}
