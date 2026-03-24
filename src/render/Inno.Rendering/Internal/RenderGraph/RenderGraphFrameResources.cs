namespace Inno.Rendering;

internal sealed class RenderGraphFrameResources
{
    private readonly Dictionary<string, CompiledRenderGraphResource> m_resourceByName = new(StringComparer.Ordinal);

    public RenderGraphFrameResources(RenderTarget outputTarget, CompiledRenderGraphResourcePlan plan)
    {
        ArgumentNullException.ThrowIfNull(outputTarget);
        ArgumentNullException.ThrowIfNull(plan);

        output = outputTarget;
        foreach (var resource in plan.resources)
        {
            m_resourceByName[resource.name] = resource;
        }
    }

    public RenderTarget output { get; }

    public bool TryGetDescriptor(string resourceName, out RenderTargetDescriptor? descriptor)
    {
        if (m_resourceByName.TryGetValue(resourceName, out var resource))
        {
            descriptor = resource.descriptor;
            return true;
        }

        descriptor = null;
        return false;
    }

    public bool TryResolveRenderTarget(string resourceName, out RenderTarget? target)
    {
        if (string.Equals(resourceName, RenderGraphResourceNames.Backbuffer, StringComparison.Ordinal))
        {
            target = output;
            return true;
        }

        if (m_resourceByName.TryGetValue(resourceName, out var resource) && resource.isExternal)
        {
            target = output;
            return true;
        }

        target = null;
        return false;
    }

    public bool TryGetInternalDescriptor(string resourceName, out RenderTargetDescriptor? descriptor)
    {
        if (m_resourceByName.TryGetValue(resourceName, out var resource) && !resource.isExternal && resource.descriptor is not null)
        {
            descriptor = resource.descriptor;
            return true;
        }

        descriptor = null;
        return false;
    }
}
