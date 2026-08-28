using System;

namespace Inno.Rendering.Core;

/// <summary>
/// Identifies a portable encoded texture container accepted by a render device.
/// </summary>
public enum RenderTextureContainer
{
    /// <summary>Khronos Texture container containing validated GPU texture payloads.</summary>
    Ktx
}

/// <summary>
/// Owns one graphics backend generation and its frame submission boundary.
/// </summary>
public interface IRenderDevice : IDisposable
{
    /// <summary>Gets immutable capabilities for the active device generation.</summary>
    GraphicsCapabilities capabilities { get; }

    /// <summary>Gets the non-zero device generation used to reject stale persistent handles.</summary>
    uint generation { get; }

    /// <summary>Begins the sole API-thread frame scope and processes queued resource work.</summary>
    void BeginFrame();

    /// <summary>Executes one compiled graph without presenting or advancing another frame.</summary>
    /// <param name="graph">Compiled graph to execute.</param>
    /// <param name="frameIndex">Monotonic engine render frame index.</param>
    void Execute(CompiledRenderGraph graph, ulong frameIndex);

    /// <summary>Ends all encoders and advances the graphics backend exactly once.</summary>
    /// <returns>The backend frame number after submission.</returns>
    uint EndFrame();

    /// <summary>Queues a backbuffer resize for the current API-thread safety point.</summary>
    /// <param name="width">Backbuffer width in pixels.</param>
    /// <param name="height">Backbuffer height in pixels.</param>
    void ResizeBackbuffer(int width, int height);

    /// <summary>Creates a persistent texture at a frame safety point.</summary>
    /// <param name="descriptor">Texture requirements.</param>
    /// <param name="name">Debug and diagnostic name.</param>
    /// <returns>An opaque device-generation handle.</returns>
    PersistentTextureHandle CreateTexture(RenderTextureDescriptor descriptor, string name);

    /// <summary>Creates a persistent sampled texture from a validated portable container.</summary>
    /// <param name="container">Portable texture container kind.</param>
    /// <param name="data">Complete encoded container bytes.</param>
    /// <param name="sRgb">Whether sampling decodes the stored color from sRGB.</param>
    /// <param name="name">Debug and diagnostic name.</param>
    /// <returns>An opaque device-generation handle.</returns>
    /// <exception cref="NotSupportedException">Thrown when the device cannot consume encoded containers.</exception>
    PersistentTextureHandle CreateTexture(
        RenderTextureContainer container,
        ReadOnlySpan<byte> data,
        bool sRgb,
        string name)
        => throw new NotSupportedException("This render device does not support encoded texture containers.");

    /// <summary>Replaces one complete mip and array layer of a persistent texture.</summary>
    /// <param name="texture">Texture owned by this device generation.</param>
    /// <param name="data">Tightly packed complete subresource bytes.</param>
    /// <param name="mipLevel">Zero-based mip level.</param>
    /// <param name="arrayLayer">Zero-based array layer.</param>
    void UpdateTexture(
        PersistentTextureHandle texture,
        ReadOnlySpan<byte> data,
        int mipLevel = 0,
        int arrayLayer = 0);

    /// <summary>Queues a persistent texture for delayed GPU-safe destruction.</summary>
    /// <param name="texture">Texture owned by this device generation.</param>
    void DestroyTexture(PersistentTextureHandle texture);

    /// <summary>Creates a persistent vertex, index or storage buffer at a frame safety point.</summary>
    /// <param name="descriptor">Buffer capacity and interpretation.</param>
    /// <param name="initialData">Optional complete initial contents.</param>
    /// <param name="name">Debug and diagnostic name.</param>
    /// <returns>An opaque device-generation buffer handle.</returns>
    PersistentBufferHandle CreateBuffer(
        PersistentBufferDescriptor descriptor,
        ReadOnlySpan<byte> initialData,
        string name);

    /// <summary>Replaces a contiguous range in a dynamic persistent buffer at a frame safety point.</summary>
    /// <param name="buffer">Dynamic buffer owned by this device generation.</param>
    /// <param name="data">Complete replacement elements.</param>
    /// <param name="startElement">Zero-based destination element.</param>
    void UpdateBuffer(
        PersistentBufferHandle buffer,
        ReadOnlySpan<byte> data,
        int startElement = 0);

    /// <summary>Queues a persistent buffer for delayed GPU-safe destruction.</summary>
    /// <param name="buffer">Buffer owned by this device generation.</param>
    void DestroyBuffer(PersistentBufferHandle buffer);

    /// <summary>Creates and reflection-validates a graphics pipeline at a frame safety point.</summary>
    /// <param name="descriptor">Compiled stages, interface, layout and raster state.</param>
    /// <param name="name">Debug and diagnostic name.</param>
    /// <returns>An opaque device-generation graphics pipeline handle.</returns>
    GraphicsPipelineHandle CreateGraphicsPipeline(GraphicsPipelineDescriptor descriptor, string name);

    /// <summary>Queues a graphics pipeline for delayed GPU-safe destruction.</summary>
    /// <param name="pipeline">Pipeline owned by this device generation.</param>
    void DestroyGraphicsPipeline(GraphicsPipelineHandle pipeline);

    /// <summary>Creates and reflection-validates a compute pipeline at a frame safety point.</summary>
    /// <param name="descriptor">Compiled stage and interface contract.</param>
    /// <param name="name">Debug and diagnostic name.</param>
    /// <returns>An opaque device-generation compute pipeline handle.</returns>
    ComputePipelineHandle CreateComputePipeline(ComputePipelineDescriptor descriptor, string name);

    /// <summary>Queues a compute pipeline for delayed GPU-safe destruction.</summary>
    /// <param name="pipeline">Pipeline owned by this device generation.</param>
    void DestroyComputePipeline(ComputePipelineHandle pipeline);
}
