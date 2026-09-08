using System;
using Inno.Rendering;

namespace Inno.Rendering;

/// <summary>
/// Describes a persistent offscreen target without owning a backend-native handle.
/// </summary>
public sealed class RenderTexture
{
    private RenderTextureDescriptor m_descriptor;

    /// <summary>
    /// Creates an offscreen render target description.
    /// </summary>
    /// <param name="name">
    /// Artist-facing and diagnostic name.
    /// </param>
    /// <param name="descriptor">
    /// Initial texture requirements.
    /// </param>
    public RenderTexture(string name, RenderTextureDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(descriptor);
        this.name = name;
        m_descriptor = descriptor;
    }

    /// <summary>
    /// Gets the artist-facing and diagnostic name.
    /// </summary>
    public string name { get; }

    /// <summary>
    /// Gets the current texture requirements.
    /// </summary>
    public RenderTextureDescriptor descriptor => m_descriptor;

    /// <summary>
    /// Gets a counter incremented whenever the descriptor changes.
    /// </summary>
    public long contentRevision { get; private set; }

    /// <summary>
    /// Replaces texture requirements at the next render-frame safety point.
    /// </summary>
    /// <param name="descriptor">
    /// New texture requirements.
    /// </param>
    public void Resize(RenderTextureDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (m_descriptor.Equals(descriptor))
        {
            return;
        }

        m_descriptor = descriptor;
        contentRevision++;
    }
}

/// <summary>
/// Identifies whether a request renders to the main swapchain or an offscreen target.
/// </summary>
public enum RenderTargetKind
{
    /// <summary>
    /// Main application window swapchain.
    /// </summary>
    Backbuffer,
    /// <summary>
    /// Persistent offscreen render texture.
    /// </summary>
    Texture
}

/// <summary>
/// Selects one render destination without exposing a swapchain or framebuffer handle.
/// </summary>
public readonly record struct RenderTarget
{
    private RenderTarget(RenderTargetKind kind, RenderTexture? texture)
    {
        this.kind = kind;
        this.texture = texture;
    }

    /// <summary>
    /// Gets a target representing the main application backbuffer.
    /// </summary>
    public static RenderTarget backbuffer { get; } = new(RenderTargetKind.Backbuffer, null);

    /// <summary>
    /// Gets the target kind.
    /// </summary>
    public RenderTargetKind kind { get; }

    /// <summary>
    /// Gets the offscreen texture, or <see langword="null"/> for the backbuffer.
    /// </summary>
    public RenderTexture? texture { get; }

    /// <summary>
    /// Creates an offscreen render target.
    /// </summary>
    /// <param name="texture">
    /// Persistent offscreen texture description.
    /// </param>
    /// <returns>
    /// A target referencing <paramref name="texture"/>.
    /// </returns>
    public static RenderTarget FromTexture(RenderTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        return new RenderTarget(RenderTargetKind.Texture, texture);
    }
}
