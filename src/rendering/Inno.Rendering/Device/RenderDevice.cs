namespace Inno.Rendering;

/// <summary>
/// Provides the protected opaque-handle boundary required by replaceable render-device backends.
/// </summary>
/// <remarks>
/// Backend implementations derive from this type so they can encode and validate backend-neutral
/// handles without exposing raw backend identities to application or scripting code.
/// </remarks>
public abstract class RenderDevice
{
    /// <summary>
    /// Stores one decoded persistent device identity for use inside a concrete backend.
    /// </summary>
    /// <param name="value">
    /// The backend-owned non-zero resource identity.
    /// </param>
    /// <param name="generation">
    /// The device generation that owns the resource.
    /// </param>
    protected readonly record struct DeviceHandleIdentity(ulong value, uint generation);

    /// <summary>
    /// Stores one decoded frame-scoped graph identity for use inside a concrete backend.
    /// </summary>
    /// <param name="index">
    /// The zero-based logical resource index.
    /// </param>
    /// <param name="generation">
    /// The render-graph generation that owns the resource.
    /// </param>
    protected readonly record struct GraphHandleIdentity(int index, uint generation);

    /// <summary>
    /// Encodes a persistent texture identity into a backend-neutral handle.
    /// </summary>
    /// <param name="value">
    /// The backend-owned non-zero texture identity.
    /// </param>
    /// <param name="generation">
    /// The device generation that owns the texture.
    /// </param>
    /// <returns>
    /// An opaque texture handle suitable for the public render-device contract.
    /// </returns>
    protected static PersistentTextureHandle CreatePersistentTextureHandle(ulong value, uint generation)
        => new(value, generation);

    /// <summary>
    /// Encodes a persistent buffer identity into a backend-neutral handle.
    /// </summary>
    /// <param name="value">
    /// The backend-owned non-zero buffer identity.
    /// </param>
    /// <param name="generation">
    /// The device generation that owns the buffer.
    /// </param>
    /// <returns>
    /// An opaque buffer handle suitable for the public render-device contract.
    /// </returns>
    protected static PersistentBufferHandle CreatePersistentBufferHandle(ulong value, uint generation)
        => new(value, generation);

    /// <summary>
    /// Encodes a graphics pipeline identity into a backend-neutral handle.
    /// </summary>
    /// <param name="value">
    /// The backend-owned non-zero pipeline identity.
    /// </param>
    /// <param name="generation">
    /// The device generation that owns the pipeline.
    /// </param>
    /// <returns>
    /// An opaque graphics pipeline handle.
    /// </returns>
    protected static GraphicsPipelineHandle CreateGraphicsPipelineHandle(ulong value, uint generation)
        => new(value, generation);

    /// <summary>
    /// Encodes a compute pipeline identity into a backend-neutral handle.
    /// </summary>
    /// <param name="value">
    /// The backend-owned non-zero pipeline identity.
    /// </param>
    /// <param name="generation">
    /// The device generation that owns the pipeline.
    /// </param>
    /// <returns>
    /// An opaque compute pipeline handle.
    /// </returns>
    protected static ComputePipelineHandle CreateComputePipelineHandle(ulong value, uint generation)
        => new(value, generation);

    /// <summary>
    /// Encodes a presentation-surface identity into a backend-neutral handle.
    /// </summary>
    /// <param name="value">
    /// The backend-owned non-zero surface identity.
    /// </param>
    /// <param name="generation">
    /// The device generation that owns the surface.
    /// </param>
    /// <returns>
    /// An opaque presentation-surface handle.
    /// </returns>
    protected static RenderSurfaceHandle CreateRenderSurfaceHandle(ulong value, uint generation)
        => new(value, generation);

    /// <summary>
    /// Encodes a texture-readback identity into a backend-neutral handle.
    /// </summary>
    /// <param name="value">
    /// The backend-owned non-zero readback identity.
    /// </param>
    /// <param name="generation">
    /// The device generation that owns the readback.
    /// </param>
    /// <returns>
    /// An opaque texture-readback handle.
    /// </returns>
    protected static RenderTextureReadbackHandle CreateRenderTextureReadbackHandle(ulong value, uint generation)
        => new(value, generation);

    /// <summary>
    /// Decodes a persistent texture handle for backend lookup and generation validation.
    /// </summary>
    /// <param name="handle">
    /// The backend-neutral texture handle.
    /// </param>
    /// <returns>
    /// The backend resource identity and owning device generation.
    /// </returns>
    protected static DeviceHandleIdentity GetHandleIdentity(PersistentTextureHandle handle)
        => new(handle.value, handle.deviceGeneration);

    /// <summary>
    /// Decodes a persistent buffer handle for backend lookup and generation validation.
    /// </summary>
    /// <param name="handle">
    /// The backend-neutral buffer handle.
    /// </param>
    /// <returns>
    /// The backend resource identity and owning device generation.
    /// </returns>
    protected static DeviceHandleIdentity GetHandleIdentity(PersistentBufferHandle handle)
        => new(handle.value, handle.deviceGeneration);

    /// <summary>
    /// Decodes a graphics pipeline handle for backend lookup and generation validation.
    /// </summary>
    /// <param name="handle">
    /// The backend-neutral graphics pipeline handle.
    /// </param>
    /// <returns>
    /// The backend resource identity and owning device generation.
    /// </returns>
    protected static DeviceHandleIdentity GetHandleIdentity(GraphicsPipelineHandle handle)
        => new(handle.value, handle.deviceGeneration);

    /// <summary>
    /// Decodes a compute pipeline handle for backend lookup and generation validation.
    /// </summary>
    /// <param name="handle">
    /// The backend-neutral compute pipeline handle.
    /// </param>
    /// <returns>
    /// The backend resource identity and owning device generation.
    /// </returns>
    protected static DeviceHandleIdentity GetHandleIdentity(ComputePipelineHandle handle)
        => new(handle.value, handle.deviceGeneration);

    /// <summary>
    /// Decodes a presentation-surface handle for backend lookup and generation validation.
    /// </summary>
    /// <param name="handle">
    /// The backend-neutral presentation-surface handle.
    /// </param>
    /// <returns>
    /// The backend resource identity and owning device generation.
    /// </returns>
    protected static DeviceHandleIdentity GetHandleIdentity(RenderSurfaceHandle handle)
        => new(handle.value, handle.deviceGeneration);

    /// <summary>
    /// Decodes a texture-readback handle for backend lookup and generation validation.
    /// </summary>
    /// <param name="handle">
    /// The backend-neutral texture-readback handle.
    /// </param>
    /// <returns>
    /// The backend resource identity and owning device generation.
    /// </returns>
    protected static DeviceHandleIdentity GetHandleIdentity(RenderTextureReadbackHandle handle)
        => new(handle.value, handle.deviceGeneration);

    /// <summary>
    /// Decodes a frame-scoped texture handle for graph resource lookup.
    /// </summary>
    /// <param name="handle">
    /// The backend-neutral graph texture handle.
    /// </param>
    /// <returns>
    /// The graph resource index and owning graph generation.
    /// </returns>
    protected static GraphHandleIdentity GetHandleIdentity(RenderTextureHandle handle)
        => new(handle.index, handle.generation);

    /// <summary>
    /// Decodes a frame-scoped buffer handle for graph resource lookup.
    /// </summary>
    /// <param name="handle">
    /// The backend-neutral graph buffer handle.
    /// </param>
    /// <returns>
    /// The graph resource index and owning graph generation.
    /// </returns>
    protected static GraphHandleIdentity GetHandleIdentity(RenderBufferHandle handle)
        => new(handle.index, handle.generation);
}
