
namespace Inno.Rendering;

/// <summary>
/// Represents the built-in forward render pipeline.
/// </summary>
public sealed class ForwardPipeline : RenderPipeline
{
    private readonly List<RenderPass> m_passes;
    private readonly RenderPassGraphCompiler m_graphCompiler = new();

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
        var graph = m_graphCompiler.Compile(m_passes);
        var frameResources = new RenderGraphFrameResources(context.request.target, graph.resourcePlan);
        context.frame.statistics.renderGraphPassCount = graph.orderedPasses.Count;
        context.frame.statistics.renderGraphResourceCount = graph.resourcePlan.resources.Count;
        var passContext = new RenderPassContext
        {
            pipelineContext = context,
            renderList = renderList,
            frameResources = frameResources
        };

        foreach (var pass in graph.orderedPasses)
        {
            if (pass.enabled)
            {
                if (graph.TryGetDeclaration(pass, out var declaration))
                {
                    context.graphics?.BeginGraphPass(pass, declaration, frameResources, context.request);
                }

                pass.Execute(passContext);
            }
        }
    }

    internal static IReadOnlyList<IForwardPassProvider> CreateDefaultPassProviders()
    {
        return
        [
            new BuiltinForwardPassProvider(static features => features.enableShadows, static () => new ShadowPass()),
            new BuiltinForwardPassProvider(static features => features.enableDepthPrepass, static () => new DepthPrepass()),
            new BuiltinForwardPassProvider(static _ => true, static () => new OpaquePass()),
            new BuiltinForwardPassProvider(static features => features.enableSkybox, static () => new SkyboxPass()),
            new BuiltinForwardPassProvider(static features => features.enableTransparentPass, static () => new TransparentPass()),
            new BuiltinForwardPassProvider(static features => features.enablePostProcessing, static () => new PostProcessPass()),
            new BuiltinForwardPassProvider(static features => features.enableObjectPicking, static () => new ObjectPickingPass()),
            new BuiltinForwardPassProvider(static features => features.enableGizmos, static () => new GizmoPass()),
            new BuiltinForwardPassProvider(static features => features.enableUiPass, static () => new UiPass())
        ];
    }

    internal static ForwardPipeline FromFeatureSet(PipelineFeatureSet features, IReadOnlyList<RenderFeature>? customFeatures = null)
    {
        return FromProviders(features, CreateDefaultPassProviders(), customFeatures);
    }

    internal static ForwardPipeline FromProviders(
        PipelineFeatureSet features,
        IReadOnlyList<IForwardPassProvider> passProviders,
        IReadOnlyList<RenderFeature>? customFeatures = null)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(passProviders);
        var passes = new List<RenderPass>();
        var providerContext = new ForwardPassProviderContext
        {
            features = features
        };
        foreach (var provider in passProviders)
        {
            provider.AddRenderPasses(providerContext, passes);
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
