namespace Inno.Rendering;

internal sealed class RenderGraphPassBuilder
{
    private readonly List<RenderGraphResourceUsage> m_resources = [];

    public RenderGraphPassBuilder Read(string resourceName)
    {
        ValidateResourceName(resourceName);
        m_resources.Add(new RenderGraphResourceUsage(resourceName, RenderGraphResourceAccess.Read, null));
        return this;
    }

    public RenderGraphPassBuilder Write(string resourceName, RenderTargetDescriptor? descriptor = null)
    {
        ValidateResourceName(resourceName);
        m_resources.Add(new RenderGraphResourceUsage(resourceName, RenderGraphResourceAccess.Write, descriptor));
        return this;
    }

    public RenderGraphPassBuilder ReadWrite(string resourceName, RenderTargetDescriptor? descriptor = null)
    {
        ValidateResourceName(resourceName);
        m_resources.Add(new RenderGraphResourceUsage(resourceName, RenderGraphResourceAccess.ReadWrite, descriptor));
        return this;
    }

    public RenderGraphPassDeclaration Build()
    {
        return new RenderGraphPassDeclaration(m_resources.ToArray());
    }

    private static void ValidateResourceName(string resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
        {
            throw new ArgumentException("Resource name is required.", nameof(resourceName));
        }
    }
}
