namespace Inno.Rendering;

/// <summary>
/// Contributes render passes to the forward pipeline composition process.
/// </summary>
public interface IForwardPassProvider
{
    void AddRenderPasses(ForwardPassProviderContext context, ICollection<RenderPass> passes);
}
