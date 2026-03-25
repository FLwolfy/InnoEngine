namespace Inno.Rendering;

internal interface IRenderItemClassifier
{
    bool CanClassify(RenderItemFilter filter);

    bool ShouldInclude(Renderable renderable, Material? material);
}
