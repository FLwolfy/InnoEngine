using System;
using System.Collections.Generic;
using Inno.Native.Bgfx;
using Inno.Rendering.Core;

namespace Inno.Rendering.Bgfx;

internal static class BgfxCapabilityMapper
{
    public static unsafe GraphicsCapabilities FromNative(bgfx.Caps* caps)
    {
        if (caps is null)
        {
            throw new InvalidOperationException("BGFX returned no device capability structure.");
        }

        GraphicsFeature features = GraphicsFeature.None;
        bgfx.CapsFlags supported = (bgfx.CapsFlags)caps->supported;
        if ((supported & bgfx.CapsFlags.Compute) != 0)
        {
            features |= GraphicsFeature.Compute;
        }

        if ((supported & bgfx.CapsFlags.ImageRw) != 0)
        {
            features |= GraphicsFeature.StorageBuffer;
        }

        if ((supported & bgfx.CapsFlags.DrawIndirect) != 0)
        {
            features |= GraphicsFeature.Indirect;
        }

        if ((supported & bgfx.CapsFlags.BlendIndependent) != 0)
        {
            features |= GraphicsFeature.IndependentBlend;
        }

        if ((supported & bgfx.CapsFlags.RendererMultithreaded) != 0)
        {
            features |= GraphicsFeature.ConcurrentEncoders;
        }

        if ((supported & bgfx.CapsFlags.TextureBlit) != 0)
        {
            features |= GraphicsFeature.TextureBlit;
        }

        List<RenderTextureFormat> renderTargets = [];
        List<RenderTextureFormat> storage = [];
        foreach (RenderTextureFormat format in Enum.GetValues<RenderTextureFormat>())
        {
            bgfx.TextureFormat nativeFormat = ToNativeFormat(format);
            bgfx.CapsFormatFlags formatCaps = (bgfx.CapsFormatFlags)caps->formats[(int)nativeFormat];
            if ((formatCaps & bgfx.CapsFormatFlags.TextureFramebuffer) != 0)
            {
                renderTargets.Add(format);
            }

            if ((formatCaps & bgfx.CapsFormatFlags.TextureImageWrite) != 0)
            {
                storage.Add(format);
            }
        }

        return new GraphicsCapabilities(
            ToGraphicsBackend(caps->rendererType),
            features,
            new GraphicsLimits(
                checked((int)caps->limits.maxViews),
                checked((int)caps->limits.maxFBAttachments),
                checked((int)caps->limits.maxTextureSize),
                checked((int)caps->limits.maxComputeBindings)),
            renderTargets,
            storage,
            caps->originBottomLeft != 0,
            caps->homogeneousDepth != 0);
    }

    public static bgfx.RendererType ToNativeRenderer(GraphicsBackend backend)
        => backend switch
        {
            GraphicsBackend.Noop => bgfx.RendererType.Noop,
            GraphicsBackend.Direct3D11 => bgfx.RendererType.Direct3D11,
            GraphicsBackend.Direct3D12 => bgfx.RendererType.Direct3D12,
            GraphicsBackend.Metal => bgfx.RendererType.Metal,
            GraphicsBackend.Vulkan => bgfx.RendererType.Vulkan,
            GraphicsBackend.OpenGL => bgfx.RendererType.OpenGL,
            GraphicsBackend.OpenGLES => bgfx.RendererType.OpenGLES,
            GraphicsBackend.WebGPU => bgfx.RendererType.WebGPU,
            _ => throw new ArgumentOutOfRangeException(nameof(backend))
        };

    public static GraphicsBackend ToGraphicsBackend(bgfx.RendererType backend)
        => backend switch
        {
            bgfx.RendererType.Noop => GraphicsBackend.Noop,
            bgfx.RendererType.Direct3D11 => GraphicsBackend.Direct3D11,
            bgfx.RendererType.Direct3D12 => GraphicsBackend.Direct3D12,
            bgfx.RendererType.Metal => GraphicsBackend.Metal,
            bgfx.RendererType.Vulkan => GraphicsBackend.Vulkan,
            bgfx.RendererType.OpenGL => GraphicsBackend.OpenGL,
            bgfx.RendererType.OpenGLES => GraphicsBackend.OpenGLES,
            bgfx.RendererType.WebGPU => GraphicsBackend.WebGPU,
            _ => throw new NotSupportedException($"BGFX renderer '{backend}' has no public Rendering.Core mapping.")
        };

    public static bgfx.TextureFormat ToNativeFormat(RenderTextureFormat format)
        => format switch
        {
            RenderTextureFormat.R8 => bgfx.TextureFormat.R8,
            RenderTextureFormat.RG8 => bgfx.TextureFormat.RG8,
            RenderTextureFormat.RGBA8 => bgfx.TextureFormat.RGBA8,
            RenderTextureFormat.RGBA8Srgb => bgfx.TextureFormat.RGBA8,
            RenderTextureFormat.RGB10A2 => bgfx.TextureFormat.RGB10A2,
            RenderTextureFormat.RG11B10Float => bgfx.TextureFormat.RG11B10F,
            RenderTextureFormat.RGBA16Float => bgfx.TextureFormat.RGBA16F,
            RenderTextureFormat.R32Float => bgfx.TextureFormat.R32F,
            RenderTextureFormat.Depth24Stencil8 => bgfx.TextureFormat.D24S8,
            RenderTextureFormat.Depth32Float => bgfx.TextureFormat.D32F,
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
}
