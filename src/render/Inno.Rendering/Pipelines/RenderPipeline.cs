
namespace Inno.Rendering;

/// <summary>
/// Represents an extensible render pipeline.
/// </summary>
public abstract class RenderPipeline
{
    protected RenderPipeline(string name)
    {
        this.name = name;
    }

    public string name { get; }

    internal abstract void Render(RenderPipelineContext context);
}

/// <summary>
/// Represents built-in forward render pipeline.
/// </summary>
public sealed class ForwardPipeline : RenderPipeline
{
    private readonly List<RenderPass> m_passes;

    private ForwardPipeline(string name, List<RenderPass> passes) : base(name)
    {
        m_passes = passes;
    }

    public static ForwardPipeline Create(Action<ForwardPipelineBuilder>? configure = null)
    {
        var builder = new ForwardPipelineBuilder();
        configure?.Invoke(builder);
        return builder.Build();
    }

    internal override void Render(RenderPipelineContext context)
    {
        var renderList = new RenderList(context.request.scene, context.request.view);
        var passContext = new RenderPassContext
        {
            pipelineContext = context,
            renderList = renderList
        };

        foreach (var pass in m_passes)
        {
            if (pass.enabled)
            {
                pass.Execute(passContext);
            }
        }
    }

    internal static ForwardPipeline FromFeatureSet(PipelineFeatureSet features, IReadOnlyList<RenderFeature>? customFeatures = null)
    {
        var passes = new List<RenderPass>();

        if (features.enableShadows)
        {
            passes.Add(new ShadowPass());
        }

        if (features.enableDepthPrepass)
        {
            passes.Add(new DepthPrepass());
        }

        passes.Add(new OpaquePass());

        if (features.enableSkybox)
        {
            passes.Add(new SkyboxPass());
        }

        if (features.enableTransparentPass)
        {
            passes.Add(new TransparentPass());
        }

        if (features.enablePostProcessing)
        {
            passes.Add(new PostProcessPass());
        }

        if (features.enableObjectPicking)
        {
            passes.Add(new ObjectPickingPass());
        }

        if (features.enableGizmos)
        {
            passes.Add(new GizmoPass());
        }

        if (features.enableUiPass)
        {
            passes.Add(new UiPass());
        }

        if (customFeatures is not null && customFeatures.Count > 0)
        {
            var context = new RenderFeatureContext
            {
                features = features
            };

            foreach (var feature in customFeatures)
            {
                if (feature.enabled)
                {
                    feature.AddRenderPasses(context, passes);
                }
            }
        }

        passes.Sort(static (a, b) =>
        {
            var cmp = ((int)a.passEvent).CompareTo((int)b.passEvent);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.name, b.name);
        });

        return new ForwardPipeline("ForwardPipeline", passes);
    }
}

/// <summary>
/// Represents mutable settings for creating forward pipeline.
/// </summary>
public sealed class ForwardPipelineBuilder
{
    private readonly List<RenderFeature> m_features = [];

    public bool enableDepthPrepass { get; set; }

    public bool enableShadows { get; set; }

    public bool enableSkybox { get; set; } = true;

    public bool enableTransparentPass { get; set; } = true;

    public bool enablePostProcessing { get; set; } = true;

    public bool enableGizmos { get; set; }

    public bool enableObjectPicking { get; set; }

    public bool enableUiPass { get; set; } = true;

    public ForwardPipelineBuilder AddFeature(RenderFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        m_features.Add(feature);
        return this;
    }

    public ForwardPipeline Build()
    {
        var features = new PipelineFeatureSet
        {
            enableDepthPrepass = enableDepthPrepass,
            enableShadows = enableShadows,
            enableSkybox = enableSkybox,
            enableTransparentPass = enableTransparentPass,
            enablePostProcessing = enablePostProcessing,
            enableGizmos = enableGizmos,
            enableObjectPicking = enableObjectPicking,
            enableUiPass = enableUiPass
        };

        return ForwardPipeline.FromFeatureSet(features, m_features);
    }
}

/// <summary>
/// Represents serialized pipeline asset metadata.
/// </summary>
public sealed class RenderPipelineAsset
{
    public string name { get; set; } = "ForwardPipeline";

    public PipelineFeatureSet features { get; set; } = new();

    public List<RenderFeature> customFeatures { get; } = [];

    public RenderPipeline CreatePipeline() => ForwardPipeline.FromFeatureSet(features, customFeatures);
}

/// <summary>
/// Represents feature toggles for built-in forward pipeline.
/// </summary>
public sealed class PipelineFeatureSet
{
    public bool enableDepthPrepass { get; set; }

    public bool enableShadows { get; set; }

    public bool enableSkybox { get; set; } = true;

    public bool enableTransparentPass { get; set; } = true;

    public bool enablePostProcessing { get; set; } = true;

    public bool enableGizmos { get; set; }

    public bool enableObjectPicking { get; set; }

    public bool enableUiPass { get; set; } = true;
}

/// <summary>
/// Internal pipeline execution context.
/// </summary>
internal sealed class RenderPipelineContext
{
    public required RenderRequest request { get; init; }

    public required RenderFrame frame { get; init; }

    public required RenderResourceCache resourceCache { get; init; }

    public GraphicsRenderRuntime? graphics { get; init; }
}
