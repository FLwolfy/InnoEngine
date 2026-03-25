namespace Inno.Rendering;

internal sealed class RenderItemClassifierRegistry
{
    private readonly List<IRenderItemClassifier> m_classifiers = [];

    public void Register(IRenderItemClassifier classifier)
    {
        ArgumentNullException.ThrowIfNull(classifier);
        m_classifiers.Add(classifier);
    }

    public bool ShouldInclude(RenderItemFilter filter, Renderable renderable, Material? material)
    {
        foreach (var classifier in m_classifiers)
        {
            if (!classifier.CanClassify(filter))
            {
                continue;
            }

            if (classifier.ShouldInclude(renderable, material))
            {
                return true;
            }
        }

        return false;
    }
}
