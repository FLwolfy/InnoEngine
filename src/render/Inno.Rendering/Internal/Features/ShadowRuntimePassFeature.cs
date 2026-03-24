namespace Inno.Rendering;

internal sealed class ShadowRuntimePassFeature : IRuntimePassFeature
{
    private readonly GraphicsRenderRuntime m_runtime;

    public ShadowRuntimePassFeature(GraphicsRenderRuntime runtime)
    {
        m_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public bool CanExecute(RenderItemFilter filter)
    {
        return filter == RenderItemFilter.ShadowCasters;
    }

    public void Execute(RenderPipelineContext context, RenderList renderList, RenderItemFilter filter)
    {
        _ = filter;
        m_runtime.ExecuteShadowPassFeature(context, renderList);
    }
}
