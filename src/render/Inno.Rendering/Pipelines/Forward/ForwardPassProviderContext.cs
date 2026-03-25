namespace Inno.Rendering;

/// <summary>
/// Provides immutable inputs for forward pass providers.
/// </summary>
public sealed class ForwardPassProviderContext
{
    public required PipelineFeatureSet features { get; init; }
}
