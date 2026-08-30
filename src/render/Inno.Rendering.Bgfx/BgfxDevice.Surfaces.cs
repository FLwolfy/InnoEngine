using System;
using System.Collections.Generic;
using Inno.Native.Bgfx;
using Inno.Platform;
using Inno.Rendering.Core;

namespace Inno.Rendering.Bgfx;

public sealed unsafe partial class BgfxDevice
{
    private readonly Dictionary<ulong, BgfxWindowSurfaceResource> m_windowSurfaces = [];

    /// <summary>
    /// Creates a detached-window presentation surface at a frame safety point.
    /// </summary>
    /// <param name="nativeHandles">Platform window handles supplied by the active window backend.</param>
    /// <param name="width">Drawable width in pixels.</param>
    /// <param name="height">Drawable height in pixels.</param>
    /// <param name="name">Debug and diagnostic name.</param>
    /// <returns>An opaque surface handle consumable by <see cref="RasterPassBuilder.UseSurface"/>.</returns>
    /// <exception cref="PlatformNotSupportedException">Thrown for unsupported native handle kinds.</exception>
    /// <exception cref="InvalidOperationException">Thrown when BGFX cannot create the window framebuffer.</exception>
    public RenderSurfaceHandle CreateWindowSurface(
        PlatformNativeHandles nativeHandles,
        int width,
        int height,
        string name)
    {
        EnsureSurfaceSafetyPoint();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ValidateWindowHandles(nativeHandles);
        if (!capabilities.Supports(GraphicsFeature.SwapChain))
        {
            throw new NotSupportedException(
                "The active graphics backend does not support additional presentation surfaces.");
        }

        bgfx.FrameBufferHandle frameBuffer = CreateNativeWindowSurface(nativeHandles, width, height);
        if (!frameBuffer.Valid)
        {
            throw new InvalidOperationException($"BGFX could not create detached window surface '{name}'.");
        }

        ulong id = m_nextPersistentId++;
        m_windowSurfaces.Add(id, new BgfxWindowSurfaceResource(frameBuffer, width, height, nativeHandles, name));
        return new RenderSurfaceHandle(id, generation);
    }

    /// <summary>Recreates a detached-window presentation surface for a new drawable extent.</summary>
    /// <param name="surface">Surface owned by this device generation.</param>
    /// <param name="width">New drawable width in pixels.</param>
    /// <param name="height">New drawable height in pixels.</param>
    /// <exception cref="ArgumentException">Thrown when the surface is stale or no longer active.</exception>
    public void ResizeWindowSurface(RenderSurfaceHandle surface, int width, int height)
    {
        EnsureSurfaceSafetyPoint();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        BgfxWindowSurfaceResource resource = ResolveSurface(surface);
        if (resource.width == width && resource.height == height)
        {
            return;
        }

        bgfx.FrameBufferHandle replacement = CreateNativeWindowSurface(resource.nativeHandles, width, height);
        if (!replacement.Valid)
        {
            throw new InvalidOperationException($"BGFX could not resize detached window surface '{resource.name}'.");
        }

        bgfx.FrameBufferHandle previous = resource.frameBuffer;
        resource.frameBuffer = replacement;
        resource.width = width;
        resource.height = height;
        EnqueueDestroy(DeferredResource.ForFrameBuffer(previous));
    }

    /// <summary>Queues a detached-window presentation surface for GPU-safe destruction.</summary>
    /// <param name="surface">Surface owned by this device generation.</param>
    /// <exception cref="ArgumentException">Thrown when the surface is stale or no longer active.</exception>
    public void DestroyWindowSurface(RenderSurfaceHandle surface)
    {
        EnsureSurfaceSafetyPoint();
        ValidatePersistentHandle(surface);
        if (!m_windowSurfaces.Remove(surface.value, out BgfxWindowSurfaceResource? resource))
        {
            throw new ArgumentException("Presentation surface is not active on this device.", nameof(surface));
        }

        EnqueueDestroy(DeferredResource.ForFrameBuffer(resource.frameBuffer));
    }

    private static bgfx.FrameBufferHandle CreateNativeWindowSurface(
        PlatformNativeHandles nativeHandles,
        int width,
        int height)
        => bgfx.create_frame_buffer_from_nwh(
            nativeHandles.windowHandle.ToPointer(),
            checked((ushort)width),
            checked((ushort)height),
            bgfx.TextureFormat.BGRA8,
            bgfx.TextureFormat.D24S8);

    private BgfxWindowSurfaceResource ResolveSurface(RenderSurfaceHandle surface)
    {
        ValidatePersistentHandle(surface);
        if (!m_windowSurfaces.TryGetValue(surface.value, out BgfxWindowSurfaceResource? resource))
        {
            throw new ArgumentException("Presentation surface is not active on this device.", nameof(surface));
        }

        return resource;
    }

    private static void ValidateWindowHandles(PlatformNativeHandles handles)
    {
        if (handles.windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("A native window handle is required.", nameof(handles));
        }

        if (handles.handleKind is not (PlatformNativeHandleKind.Win32 or PlatformNativeHandleKind.Cocoa))
        {
            throw new PlatformNotSupportedException(
                $"BGFX window surfaces do not support native handle kind '{handles.handleKind}'.");
        }
    }

    private void EnsureSurfaceSafetyPoint()
    {
        EnsureApiThread();
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_activeGraph is not null || m_activeEncoder is not null)
        {
            throw new InvalidOperationException(
                "Window surface changes require an API-thread point outside graph execution.");
        }
    }

    private sealed class BgfxWindowSurfaceResource
    {
        public BgfxWindowSurfaceResource(
            bgfx.FrameBufferHandle frameBuffer,
            int width,
            int height,
            PlatformNativeHandles nativeHandles,
            string name)
        {
            this.frameBuffer = frameBuffer;
            this.width = width;
            this.height = height;
            this.nativeHandles = nativeHandles;
            this.name = name;
        }

        public bgfx.FrameBufferHandle frameBuffer { get; set; }
        public int width { get; set; }
        public int height { get; set; }
        public PlatformNativeHandles nativeHandles { get; }
        public string name { get; }
    }
}
