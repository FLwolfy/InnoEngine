using System;
using System.Collections.Generic;

namespace Inno.Rendering.Core;

/// <summary>
/// Identifies a graphics API family without exposing a backend-native enum.
/// </summary>
public enum GraphicsBackend
{
    /// <summary>Headless validation backend.</summary>
    Noop,
    /// <summary>Direct3D 11 renderer.</summary>
    Direct3D11,
    /// <summary>Direct3D 12 renderer.</summary>
    Direct3D12,
    /// <summary>Apple Metal renderer.</summary>
    Metal,
    /// <summary>Khronos Vulkan renderer.</summary>
    Vulkan,
    /// <summary>Desktop OpenGL renderer.</summary>
    OpenGL,
    /// <summary>OpenGL ES renderer.</summary>
    OpenGLES,
    /// <summary>WebGPU renderer.</summary>
    WebGPU
}

/// <summary>
/// Declares backend-neutral optional graphics functionality.
/// </summary>
[Flags]
public enum GraphicsFeature
{
    /// <summary>No optional feature.</summary>
    None = 0,
    /// <summary>Compute shader dispatch.</summary>
    Compute = 1 << 0,
    /// <summary>Shader-readable and writable storage buffers.</summary>
    StorageBuffer = 1 << 1,
    /// <summary>Indirect draw and dispatch commands.</summary>
    Indirect = 1 << 2,
    /// <summary>Independent blend state per color attachment.</summary>
    IndependentBlend = 1 << 3,
    /// <summary>Multiple command encoders may record concurrently.</summary>
    ConcurrentEncoders = 1 << 4,
    /// <summary>Texture copy operations are supported directly.</summary>
    TextureBlit = 1 << 5
}

/// <summary>
/// Captures device limits used to validate and compile render graphs.
/// </summary>
public sealed class GraphicsLimits
{
    /// <summary>
    /// Creates a graphics limits snapshot.
    /// </summary>
    /// <param name="maxViews">Maximum logical backend views in one frame.</param>
    /// <param name="maxColorAttachments">Maximum color attachments in one raster pass.</param>
    /// <param name="maxTextureSize">Maximum two-dimensional texture extent.</param>
    /// <param name="maxComputeBindings">Maximum storage bindings in one compute pass.</param>
    public GraphicsLimits(int maxViews, int maxColorAttachments, int maxTextureSize, int maxComputeBindings)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxViews);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxColorAttachments);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTextureSize);
        ArgumentOutOfRangeException.ThrowIfNegative(maxComputeBindings);
        this.maxViews = maxViews;
        this.maxColorAttachments = maxColorAttachments;
        this.maxTextureSize = maxTextureSize;
        this.maxComputeBindings = maxComputeBindings;
    }

    /// <summary>Gets the maximum logical backend views in one frame.</summary>
    public int maxViews { get; }

    /// <summary>Gets the maximum color attachments in one raster pass.</summary>
    public int maxColorAttachments { get; }

    /// <summary>Gets the maximum two-dimensional texture extent.</summary>
    public int maxTextureSize { get; }

    /// <summary>Gets the maximum storage bindings in one compute pass.</summary>
    public int maxComputeBindings { get; }
}

/// <summary>
/// Provides an immutable capability snapshot for one device generation.
/// </summary>
public sealed class GraphicsCapabilities
{
    private readonly HashSet<RenderTextureFormat> m_renderTargetFormats;
    private readonly HashSet<RenderTextureFormat> m_storageFormats;

    /// <summary>
    /// Creates a device capability snapshot.
    /// </summary>
    /// <param name="backend">Active graphics API family.</param>
    /// <param name="features">Supported optional features.</param>
    /// <param name="limits">Device limits.</param>
    /// <param name="renderTargetFormats">Formats valid as raster attachments.</param>
    /// <param name="storageFormats">Formats valid for unordered shader access.</param>
    /// <param name="originBottomLeft">Whether render-target coordinates start at the bottom-left.</param>
    /// <param name="homogeneousDepth">Whether clip-space depth uses the negative-one-to-one range.</param>
    public GraphicsCapabilities(
        GraphicsBackend backend,
        GraphicsFeature features,
        GraphicsLimits limits,
        IEnumerable<RenderTextureFormat> renderTargetFormats,
        IEnumerable<RenderTextureFormat> storageFormats,
        bool originBottomLeft,
        bool homogeneousDepth)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(renderTargetFormats);
        ArgumentNullException.ThrowIfNull(storageFormats);
        this.backend = backend;
        this.features = features;
        this.limits = limits;
        this.originBottomLeft = originBottomLeft;
        this.homogeneousDepth = homogeneousDepth;
        m_renderTargetFormats = new HashSet<RenderTextureFormat>(renderTargetFormats);
        m_storageFormats = new HashSet<RenderTextureFormat>(storageFormats);
    }

    /// <summary>Gets the active graphics API family.</summary>
    public GraphicsBackend backend { get; }

    /// <summary>Gets all supported optional features.</summary>
    public GraphicsFeature features { get; }

    /// <summary>Gets device limits.</summary>
    public GraphicsLimits limits { get; }

    /// <summary>Gets whether render-target coordinates start at the bottom-left.</summary>
    public bool originBottomLeft { get; }

    /// <summary>Gets whether clip-space depth uses the negative-one-to-one range.</summary>
    public bool homogeneousDepth { get; }

    /// <summary>
    /// Tests whether every requested optional feature is supported.
    /// </summary>
    /// <param name="required">Required feature mask.</param>
    /// <returns><see langword="true"/> when every requested feature is available.</returns>
    public bool Supports(GraphicsFeature required) => (features & required) == required;

    /// <summary>
    /// Tests whether a format is valid as a raster attachment.
    /// </summary>
    /// <param name="format">Texture format to query.</param>
    /// <returns><see langword="true"/> when the format can be attached.</returns>
    public bool SupportsRenderTarget(RenderTextureFormat format) => m_renderTargetFormats.Contains(format);

    /// <summary>
    /// Tests whether a format supports unordered shader access.
    /// </summary>
    /// <param name="format">Texture format to query.</param>
    /// <returns><see langword="true"/> when the format supports unordered access.</returns>
    public bool SupportsStorage(RenderTextureFormat format) => m_storageFormats.Contains(format);
}
