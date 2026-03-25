namespace Inno.Rendering;

internal sealed class BuiltinForwardPassProvider : IForwardPassProvider
{
    private readonly Func<PipelineFeatureSet, bool> m_enabled;
    private readonly Func<RenderPass> m_factory;

    public BuiltinForwardPassProvider(Func<PipelineFeatureSet, bool> enabled, Func<RenderPass> factory)
    {
        m_enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public void AddRenderPasses(ForwardPassProviderContext context, ICollection<RenderPass> passes)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(passes);

        if (m_enabled(context.features))
        {
            passes.Add(m_factory());
        }
    }
}
