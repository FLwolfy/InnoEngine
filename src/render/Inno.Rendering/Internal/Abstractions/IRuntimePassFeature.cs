namespace Inno.Rendering;

internal interface IRuntimePassFeature
{
    bool CanExecute(RenderItemFilter filter);

    void Execute(RenderPipelineContext context, RenderList renderList, RenderItemFilter filter);
}
