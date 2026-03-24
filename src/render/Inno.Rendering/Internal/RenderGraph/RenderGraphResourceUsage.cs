namespace Inno.Rendering;

internal readonly record struct RenderGraphResourceUsage(
    string name,
    RenderGraphResourceAccess access,
    RenderTargetDescriptor? descriptor);
