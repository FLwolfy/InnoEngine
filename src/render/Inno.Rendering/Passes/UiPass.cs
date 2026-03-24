
namespace Inno.Rendering;

/// <summary>
/// Renders UI elements.
/// </summary>
public sealed class UiPass : RenderPass
{
    public UiPass() : base("UI", RenderPassEvent.Ui)
    {
    }

    internal override void Setup(RenderGraphPassBuilder builder)
    {
        builder.ReadWrite(RenderGraphResourceNames.Backbuffer);
    }

    internal override void Execute(RenderPassContext context)
    {
        context.renderList.Build(RenderItemFilter.Ui);
        context.pipelineContext.graphics?.ExecutePass(context.pipelineContext, context.renderList, RenderItemFilter.Ui);
    }
}
