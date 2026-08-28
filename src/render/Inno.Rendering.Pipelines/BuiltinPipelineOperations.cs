namespace Inno.Rendering.Pipelines;

/// <summary>
/// Provides stable operation identifiers consumed by the built-in pipeline executor.
/// </summary>
public static class BuiltinPipelineOperations
{
    /// <summary>Builds clustered light-grid and light-index buffers.</summary>
    public const string ClusterLights = "inno.pipeline.cluster-lights";

    /// <summary>Renders one directional shadow cascade.</summary>
    public const string DirectionalShadow = "inno.pipeline.directional-shadow";

    /// <summary>Renders opaque objects with forward physically based lighting.</summary>
    public const string ForwardOpaque = "inno.pipeline.forward-opaque";

    /// <summary>Writes deferred material geometry buffers.</summary>
    public const string GBuffer = "inno.pipeline.gbuffer";

    /// <summary>Resolves deferred physically based lighting.</summary>
    public const string DeferredLighting = "inno.pipeline.deferred-lighting";

    /// <summary>Draws the procedural or texture-backed sky.</summary>
    public const string Sky = "inno.pipeline.sky";

    /// <summary>Renders transparent objects after scene lighting.</summary>
    public const string Transparent = "inno.pipeline.transparent";

    /// <summary>Scene object-ID rendering for Editor picking.</summary>
    public const string Picking = "inno.pipeline.picking";

    /// <summary>Extracts and downsamples bright HDR regions.</summary>
    public const string BloomDownsample = "inno.pipeline.bloom-downsample";

    /// <summary>Upsamples and filters the Bloom pyramid.</summary>
    public const string BloomUpsample = "inno.pipeline.bloom-upsample";

    /// <summary>Applies exposure, Bloom composition and display tone mapping.</summary>
    public const string ToneMap = "inno.pipeline.tone-map";

    /// <summary>Applies exposure, a filtered Bloom contribution and display tone mapping.</summary>
    public const string ToneMapBloom = "inno.pipeline.tone-map-bloom";
}
