
namespace Inno.Rendering;

/// <summary>
/// Represents the built-in forward render pipeline.
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
