namespace Inno.Rendering;

internal sealed class RuntimePassFeatureRegistry
{
    private readonly List<IRuntimePassFeature> m_features = [];

    public void Register(IRuntimePassFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        m_features.Add(feature);
    }

    public bool TryExecute(RenderPipelineContext context, RenderList renderList, RenderItemFilter filter)
    {
        foreach (var feature in m_features)
        {
            if (!feature.CanExecute(filter))
            {
                continue;
            }

            feature.Execute(context, renderList, filter);
            return true;
        }

        return false;
    }
}
