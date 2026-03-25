namespace Inno.Rendering;

internal sealed class DefaultRenderItemClassifier : IRenderItemClassifier
{
    private readonly RenderItemFilter m_filter;
    private readonly Func<Renderable, Material?, bool> m_matcher;

    public DefaultRenderItemClassifier(RenderItemFilter filter, Func<Renderable, Material?, bool> matcher)
    {
        m_filter = filter;
        m_matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
    }

    public bool CanClassify(RenderItemFilter filter)
    {
        return m_filter == filter;
    }

    public bool ShouldInclude(Renderable renderable, Material? material)
    {
        return m_matcher(renderable, material);
    }
}
