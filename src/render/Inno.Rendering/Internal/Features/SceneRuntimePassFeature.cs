namespace Inno.Rendering;

internal sealed class SceneRuntimePassFeature : IRuntimePassFeature
{
    private readonly GraphicsRenderRuntime m_runtime;
    private readonly HashSet<RenderItemFilter> m_supportedFilters;

    public SceneRuntimePassFeature(GraphicsRenderRuntime runtime, IEnumerable<RenderItemFilter> supportedFilters)
    {
        m_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        ArgumentNullException.ThrowIfNull(supportedFilters);
        m_supportedFilters = new HashSet<RenderItemFilter>(supportedFilters);
    }

    public bool CanExecute(RenderItemFilter filter)
    {
        return m_supportedFilters.Contains(filter);
    }

    public void Execute(RenderPipelineContext context, RenderList renderList, RenderItemFilter filter)
    {
        m_runtime.ExecuteScenePassFeature(context, renderList, filter);
    }
}
