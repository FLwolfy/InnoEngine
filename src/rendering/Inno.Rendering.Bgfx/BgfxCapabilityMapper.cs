using System;
using System.Collections.Generic;
using Inno.Native.Bgfx;
using Inno.Rendering;

namespace Inno.Rendering.Bgfx;

internal static class BgfxCapabilityMapper
{
    /// <summary>
    /// Creates the target representation from the supplied native value.
    /// </summary>
    /// <param name="caps">
    /// The caps consumed by from native; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated graphics capabilities that represents the completed operation.
    /// </returns>
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
            features |= GraphicsFeature.StorageBuffer | GraphicsFeature.StorageTexture;
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

        if ((supported & bgfx.CapsFlags.AlphaToCoverage) != 0)
        {
            features |= GraphicsFeature.AlphaToCoverage;
        }

        if ((supported & bgfx.CapsFlags.Index32) != 0)
        {
            features |= GraphicsFeature.Index32;
        }

        if ((supported & bgfx.CapsFlags.Instancing) != 0)
        {
            features |= GraphicsFeature.Instancing;
        }

        if ((supported & bgfx.CapsFlags.SwapChain) != 0)
        {
            features |= GraphicsFeature.SwapChain;
        }

        if ((supported & bgfx.CapsFlags.Texture2dArray) != 0)
        {
            features |= GraphicsFeature.Texture2DArray;
        }

        if ((supported & bgfx.CapsFlags.Texture3d) != 0)
        {
            features |= GraphicsFeature.Texture3D;
        }

        if ((supported & bgfx.CapsFlags.TextureCubeArray) != 0)
        {
            features |= GraphicsFeature.TextureCubeArray;
        }

        if ((supported & bgfx.CapsFlags.VertexAttribHalf) != 0)
        {
            features |= GraphicsFeature.VertexAttributeHalf;
        }

        if ((supported & bgfx.CapsFlags.VertexAttribUint10) != 0)
        {
            features |= GraphicsFeature.VertexAttributeUInt10;
        }

        if ((supported & bgfx.CapsFlags.VertexId) != 0)
        {
            features |= GraphicsFeature.ProceduralDraw;
        }

        if ((supported & bgfx.CapsFlags.FragmentDepth) != 0)
        {
            features |= GraphicsFeature.FragmentDepth;
        }

        if ((supported & bgfx.CapsFlags.TextureReadBack) != 0)
        {
            features |= GraphicsFeature.TextureReadback;
        }

        List<RenderTextureFormat> sampled = [];
        List<RenderTextureFormat> sampled3D = [];
        List<RenderTextureFormat> sampledCube = [];
        List<RenderTextureFormat> renderTargets = [];
        List<RenderTextureFormat> multisampleRenderTargets = [];
        List<RenderTextureFormat> storageRead = [];
        List<RenderTextureFormat> storageWrite = [];
        foreach (RenderTextureFormat format in Enum.GetValues<RenderTextureFormat>())
        {
            bgfx.TextureFormat nativeFormat = ToNativeFormat(format);
            bgfx.CapsFormatFlags formatCaps = (bgfx.CapsFormatFlags)caps->formats[(int)nativeFormat];
            bgfx.CapsFormatFlags sampledFlag = format == RenderTextureFormat.RGBA8Srgb
                ? bgfx.CapsFormatFlags.Texture2dSrgb
                : bgfx.CapsFormatFlags.Texture2d;
            if ((formatCaps & sampledFlag) != 0)
            {
                sampled.Add(format);
            }

            bgfx.CapsFormatFlags sampled3DFlag = format == RenderTextureFormat.RGBA8Srgb
                ? bgfx.CapsFormatFlags.Texture3dSrgb
                : bgfx.CapsFormatFlags.Texture3d;
            if ((formatCaps & sampled3DFlag) != 0)
            {
                sampled3D.Add(format);
            }

            bgfx.CapsFormatFlags sampledCubeFlag = format == RenderTextureFormat.RGBA8Srgb
                ? bgfx.CapsFormatFlags.TextureCubeSrgb
                : bgfx.CapsFormatFlags.TextureCube;
            if ((formatCaps & sampledCubeFlag) != 0)
            {
                sampledCube.Add(format);
            }

            if ((formatCaps & bgfx.CapsFormatFlags.TextureFramebuffer) != 0)
            {
                renderTargets.Add(format);
            }

            if ((formatCaps & bgfx.CapsFormatFlags.TextureFramebufferMsaa) != 0)
            {
                multisampleRenderTargets.Add(format);
            }

            if ((formatCaps & bgfx.CapsFormatFlags.TextureImageRead) != 0)
            {
                storageRead.Add(format);
            }

            if ((formatCaps & bgfx.CapsFormatFlags.TextureImageWrite) != 0)
            {
                storageWrite.Add(format);
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
            sampled,
            renderTargets,
            storageRead,
            storageWrite,
            caps->originBottomLeft != 0,
            caps->homogeneousDepth != 0,
            sampled3D,
            sampledCube,
            multisampleRenderTargets);
    }

    /// <summary>
    /// Converts this value to its native renderer representation.
    /// </summary>
    /// <param name="backend">
    /// The backend consumed by to native renderer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated bgfx.renderer type that represents the completed operation.
    /// </returns>
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

    /// <summary>
    /// Converts this value to its graphics backend representation.
    /// </summary>
    /// <param name="backend">
    /// The backend consumed by to graphics backend; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated graphics backend that represents the completed operation.
    /// </returns>
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

    /// <summary>
    /// Converts this value to its native format representation.
    /// </summary>
    /// <param name="format">
    /// The format consumed by to native format; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated bgfx.texture format that represents the completed operation.
    /// </returns>
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
