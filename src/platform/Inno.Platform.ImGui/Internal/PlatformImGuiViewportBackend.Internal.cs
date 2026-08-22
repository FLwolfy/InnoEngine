using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

using Inno.Native.SDL3;
using Inno.Native.ImGui;
using ImGuiNative = Inno.Native.ImGui.ImGui;

namespace Inno.Platform.ImGui;

internal sealed unsafe class PlatformImGuiViewportBackend : IDisposable
{
    private sealed class ViewportWindowData
    {
        internal SDLWindowPtr window;
        internal SDLRendererPtr renderer;
        internal SDLTexturePtr fontTexture;
        internal int fontTextureWidth;
        internal int fontTextureHeight;
        internal nint fontPixelsPtr;
        internal int fontTexturePitch;
        internal int fontTextureUniqueId = -1;
        internal ImTextureRect fontUsedRect;
        internal ImTextureRect fontUpdateRect;
        internal uint windowId;
        internal GCHandle gcHandle;
        internal SDLVertex[] vertexScratch = [];
        internal int[] indexScratch = [];
    }

    private static readonly PlatformCreateWindow s_platformCreateWindow = PlatformCreateWindowCallback;
    private static readonly PlatformDestroyWindow s_platformDestroyWindow = PlatformDestroyWindowCallback;
    private static readonly PlatformShowWindow s_platformShowWindow = PlatformShowWindowCallback;
    private static readonly PlatformSetWindowPos s_platformSetWindowPos = PlatformSetWindowPosCallback;
    private static readonly PlatformSetWindowSize s_platformSetWindowSize = PlatformSetWindowSizeCallback;
    private static readonly PlatformGetWindowFramebufferScale s_platformGetWindowFramebufferScale = PlatformGetWindowFramebufferScaleCallback;
    private static readonly PlatformSetWindowFocus s_platformSetWindowFocus = PlatformSetWindowFocusCallback;
    private static readonly PlatformGetWindowFocus s_platformGetWindowFocus = PlatformGetWindowFocusCallback;
    private static readonly PlatformGetWindowMinimized s_platformGetWindowMinimized = PlatformGetWindowMinimizedCallback;
    private static readonly PlatformSetWindowTitle s_platformSetWindowTitle = PlatformSetWindowTitleCallback;
    private static readonly PlatformSetWindowAlpha s_platformSetWindowAlpha = PlatformSetWindowAlphaCallback;
    private static readonly RendererRenderWindow s_rendererRenderWindow = RendererRenderWindowCallback;
    private static readonly RendererSwapBuffers s_rendererSwapBuffers = RendererSwapBuffersCallback;
    private static readonly ImGuiPlatformIoNative.PlatformGetWindowPosCallback s_platformGetWindowPos = PlatformGetWindowPosOutCallback;
    private static readonly ImGuiPlatformIoNative.PlatformGetWindowSizeCallback s_platformGetWindowSize = PlatformGetWindowSizeOutCallback;

    private static readonly Dictionary<uint, ViewportWindowData> s_viewportsById = [];
    private static readonly Dictionary<uint, uint> s_windowToViewport = [];

    private readonly SDLWindowPtr m_mainWindow;
    private bool m_disposed;

    internal PlatformImGuiViewportBackend(PlatformWindow mainWindow)
    {
        _ = SDL.SetHint(SDL.SDL_HINT_WINDOW_ACTIVATE_WHEN_RAISED, "1");
        m_mainWindow = mainWindow.GetSdlWindow();

        var platformIo = ImGuiNative.GetPlatformIO();
        platformIo.PlatformCreateWindow = FunctionPtr(s_platformCreateWindow);
        platformIo.PlatformDestroyWindow = FunctionPtr(s_platformDestroyWindow);
        platformIo.PlatformShowWindow = FunctionPtr(s_platformShowWindow);
        platformIo.PlatformSetWindowPos = FunctionPtr(s_platformSetWindowPos);
        platformIo.PlatformSetWindowSize = FunctionPtr(s_platformSetWindowSize);
        platformIo.PlatformGetWindowFramebufferScale = FunctionPtr(s_platformGetWindowFramebufferScale);
        platformIo.PlatformSetWindowFocus = FunctionPtr(s_platformSetWindowFocus);
        platformIo.PlatformGetWindowFocus = FunctionPtr(s_platformGetWindowFocus);
        platformIo.PlatformGetWindowMinimized = FunctionPtr(s_platformGetWindowMinimized);
        platformIo.PlatformSetWindowTitle = FunctionPtr(s_platformSetWindowTitle);
        platformIo.PlatformSetWindowAlpha = FunctionPtr(s_platformSetWindowAlpha);
        platformIo.RendererRenderWindow = FunctionPtr(s_rendererRenderWindow);
        platformIo.RendererSwapBuffers = FunctionPtr(s_rendererSwapBuffers);
        ImGuiPlatformIoNative.SetPlatformGetWindowPos(platformIo, s_platformGetWindowPos);
        ImGuiPlatformIoNative.SetPlatformGetWindowSize(platformIo, s_platformGetWindowSize);

        var mainViewport = ImGuiNative.GetMainViewport();
        SDLWindowPtr mainSdlWindow = mainWindow.GetSdlWindow();
        mainViewport.PlatformHandle = (void*)mainSdlWindow.Handle;
        mainViewport.PlatformHandleRaw = (void*)mainSdlWindow.Handle;

        RefreshMonitors(mainSdlWindow);
    }

    internal bool OwnsWindow(uint windowId)
    {
        return s_windowToViewport.ContainsKey(windowId);
    }

    internal void ProcessEvent(ref SDLEvent sdlEvent, uint windowId)
    {
        if (!s_windowToViewport.TryGetValue(windowId, out var viewportId))
        {
            return;
        }

        if (!s_viewportsById.TryGetValue(viewportId, out var viewportData))
        {
            return;
        }

        var eventType = (SDLEventType)sdlEvent.Type;
        if (eventType == SDLEventType.WindowCloseRequested)
        {
            var viewport = FindViewportById(viewportId);
            if (!viewport.IsNull)
            {
                viewport.PlatformRequestClose = true;
            }
        }

        if (eventType == SDLEventType.WindowMoved)
        {
            var viewport = FindViewportById(viewportId);
            if (!viewport.IsNull)
            {
                viewport.PlatformRequestMove = true;
            }
        }

        if (eventType == SDLEventType.WindowResized || eventType == SDLEventType.WindowPixelSizeChanged)
        {
            var viewport = FindViewportById(viewportId);
            if (!viewport.IsNull)
            {
                viewport.PlatformRequestResize = true;
                viewport.DpiScale = GetWindowDpiScale(viewportData.window);
            }
        }

        if (eventType == SDLEventType.WindowDisplayScaleChanged)
        {
            var viewport = FindViewportById(viewportId);
            if (!viewport.IsNull)
            {
                viewport.DpiScale = GetWindowDpiScale(viewportData.window);
            }
        }
    }

    /// <summary>
    /// Synchronizes a secondary viewport's geometry, framebuffer scale, and renderer output.
    /// </summary>
    /// <param name="windowId">The SDL window identifier owned by the viewport.</param>
    internal void SynchronizeWindow(uint windowId)
    {
        if (!s_windowToViewport.TryGetValue(windowId, out var viewportId))
        {
            return;
        }

        var viewport = FindViewportById(viewportId);
        if (viewport.IsNull || viewport.Handle == null)
        {
            return;
        }

        if (!TryGetViewportData(viewport.Handle, out var data))
        {
            return;
        }

        var width = 0;
        var height = 0;
        SDL.GetWindowSize(data.window, ref width, ref height);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var x = 0;
        var y = 0;
        _ = SDL.GetWindowPosition(data.window, ref x, ref y);

        viewport.Handle->Pos = new Vector2(x, y);
        viewport.Handle->WorkPos = new Vector2(x, y);
        viewport.Handle->Size = new Vector2(width, height);
        viewport.Handle->WorkSize = new Vector2(width, height);
        Vector2 framebufferScale = GetWindowFramebufferScale(data.window);
        viewport.Handle->DpiScale = Math.Max(framebufferScale.X, framebufferScale.Y);
        viewport.Handle->FramebufferScale = framebufferScale;
        viewport.Handle->PlatformRequestMove = 1;
        viewport.Handle->PlatformRequestResize = 1;

        SynchronizeRendererOutput(data.renderer);
    }

    /// <summary>
    /// Transfers native focus to the owned window beneath a completed cross-window pointer drag.
    /// </summary>
    /// <param name="sourceWindowId">The SDL window identifier where the pointer press began.</param>
    internal void FocusPointerTarget(uint sourceWindowId)
    {
        if (sourceWindowId == 0)
        {
            return;
        }

        SDLWindowPtr targetWindow = ResolvePointerFocusTarget(sourceWindowId);
        uint targetWindowId = targetWindow.IsNull ? 0 : SDL.GetWindowID(targetWindow);
        if (targetWindowId != 0 && targetWindowId != sourceWindowId)
        {
            FocusWindow(targetWindow);
        }
    }

    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        foreach (var kv in s_viewportsById)
        {
            DestroyViewportWindow(kv.Value);
        }

        s_viewportsById.Clear();
        s_windowToViewport.Clear();

        var platformIo = ImGuiNative.GetPlatformIO();
        platformIo.ClearPlatformHandlers();
        platformIo.ClearRendererHandlers();

        var mainViewport = ImGuiNative.GetMainViewport();
        if (!mainViewport.IsNull)
        {
            mainViewport.PlatformHandle = null;
            mainViewport.PlatformHandleRaw = null;
        }

        m_disposed = true;
    }

    private static void PlatformCreateWindowCallback(ImGuiViewport* viewport)
    {
        if (viewport == null || viewport->PlatformUserData != null || (viewport->Flags & ImGuiViewportFlags.OwnedByApp) != 0)
        {
            return;
        }

        var flags = SDLWindowFlags.Hidden | SDLWindowFlags.Resizable | SDLWindowFlags.HighPixelDensity;
        if ((viewport->Flags & ImGuiViewportFlags.NoDecoration) != 0)
        {
            flags |= SDLWindowFlags.Borderless;
        }

        if ((viewport->Flags & ImGuiViewportFlags.NoTaskBarIcon) != 0)
        {
            flags |= SDLWindowFlags.Utility;
        }

        if ((viewport->Flags & ImGuiViewportFlags.TopMost) != 0)
        {
            flags |= SDLWindowFlags.AlwaysOnTop;
        }

        var width = Math.Max(1, (int)viewport->Size.X);
        var height = Math.Max(1, (int)viewport->Size.Y);
        var window = SDL.CreateWindow("ImGui", width, height, (ulong)flags);
        if (window.IsNull)
        {
            return;
        }

        _ = SDL.SetWindowPosition(window, (int)viewport->Pos.X, (int)viewport->Pos.Y);

        var renderer = SDL.CreateRenderer(window, (byte*)0);
        if (renderer.IsNull)
        {
            SDL.DestroyWindow(window);
            return;
        }

        _ = SDL.SetRenderDrawBlendMode(renderer, (uint)SDLBlendMode.Blend);

        var data = new ViewportWindowData
        {
            window = window,
            renderer = renderer,
            windowId = SDL.GetWindowID(window)
        };
        data.gcHandle = GCHandle.Alloc(data, GCHandleType.Normal);

        var handlePtr = GCHandle.ToIntPtr(data.gcHandle);
        viewport->PlatformUserData = (void*)handlePtr;
        viewport->RendererUserData = (void*)handlePtr;
        viewport->PlatformHandle = (void*)window.Handle;
        viewport->PlatformHandleRaw = (void*)window.Handle;

        s_viewportsById[viewport->ID] = data;
        s_windowToViewport[data.windowId] = viewport->ID;
    }

    private static void PlatformDestroyWindowCallback(ImGuiViewport* viewport)
    {
        if (viewport == null || (viewport->Flags & ImGuiViewportFlags.OwnedByApp) != 0)
        {
            return;
        }

        if (TryGetViewportData(viewport, out var data))
        {
            s_viewportsById.Remove(viewport->ID);
            s_windowToViewport.Remove(data.windowId);
            DestroyViewportWindow(data);
        }

        viewport->PlatformUserData = null;
        viewport->RendererUserData = null;
        viewport->PlatformHandle = null;
        viewport->PlatformHandleRaw = null;
    }

    private static void PlatformShowWindowCallback(ImGuiViewport* viewport)
    {
        if (!TryGetWindow(viewport, out var window))
        {
            return;
        }

        _ = SDL.ShowWindow(window);
    }

    private static void PlatformSetWindowPosCallback(ImGuiViewport* viewport, Vector2 pos)
    {
        if (!TryGetWindow(viewport, out var window))
        {
            return;
        }

        _ = SDL.SetWindowPosition(window, (int)pos.X, (int)pos.Y);
    }

    private static void PlatformGetWindowPosOutCallback(ImGuiViewport* viewport, Vector2* outPos)
    {
        if (outPos == null)
        {
            return;
        }

        if (!TryGetWindow(viewport, out var window))
        {
            *outPos = Vector2.Zero;
            return;
        }

        var x = 0;
        var y = 0;
        _ = SDL.GetWindowPosition(window, ref x, ref y);
        *outPos = new Vector2(x, y);
    }

    private static void PlatformSetWindowSizeCallback(ImGuiViewport* viewport, Vector2 size)
    {
        if (!TryGetWindow(viewport, out var window))
        {
            return;
        }

        _ = SDL.SetWindowSize(window, Math.Max(1, (int)size.X), Math.Max(1, (int)size.Y));
    }

    private static void PlatformGetWindowSizeOutCallback(ImGuiViewport* viewport, Vector2* outSize)
    {
        if (outSize == null)
        {
            return;
        }

        if (!TryGetWindow(viewport, out var window))
        {
            *outSize = Vector2.Zero;
            return;
        }

        var width = 0;
        var height = 0;
        SDL.GetWindowSize(window, ref width, ref height);
        *outSize = new Vector2(width, height);
    }

    private static Vector2 PlatformGetWindowFramebufferScaleCallback(ImGuiViewport* viewport)
    {
        if (!TryGetWindow(viewport, out var window))
        {
            return Vector2.One;
        }

        return GetWindowFramebufferScale(window);
    }

    private static void PlatformSetWindowFocusCallback(ImGuiViewport* viewport)
    {
        if (!TryGetWindow(viewport, out var window))
        {
            return;
        }

        FocusWindow(window);
    }

    private static byte PlatformGetWindowFocusCallback(ImGuiViewport* viewport)
    {
        if (!TryGetWindow(viewport, out var window))
        {
            return 0;
        }

        var flags = (SDLWindowFlags)SDL.GetWindowFlags(window);
        return (flags & SDLWindowFlags.InputFocus) != 0 ? (byte)1 : (byte)0;
    }

    private static byte PlatformGetWindowMinimizedCallback(ImGuiViewport* viewport)
    {
        if (!TryGetWindow(viewport, out var window))
        {
            return 0;
        }

        var flags = (SDLWindowFlags)SDL.GetWindowFlags(window);
        return (flags & SDLWindowFlags.Minimized) != 0 ? (byte)1 : (byte)0;
    }

    private static void PlatformSetWindowTitleCallback(ImGuiViewport* viewport, byte* title)
    {
        if (!TryGetWindow(viewport, out var window))
        {
            return;
        }

        _ = SDL.SetWindowTitle(window, title);
    }

    private static void PlatformSetWindowAlphaCallback(ImGuiViewport* viewport, float alpha)
    {
        if (!TryGetWindow(viewport, out var window))
        {
            return;
        }

        _ = SDL.SetWindowOpacity(window, alpha);
    }

    private static void RendererRenderWindowCallback(ImGuiViewport* viewport, void* renderArg)
    {
        _ = renderArg;
        if (!TryGetViewportData(viewport, out var data) || viewport == null || viewport->DrawData == null)
        {
            return;
        }

        EnsureViewportFontTexture(data);
        RenderDrawData(data, data.fontTexture, viewport->DrawData);
    }

    private static void RendererSwapBuffersCallback(ImGuiViewport* viewport, void* renderArg)
    {
        _ = renderArg;
        if (!TryGetViewportData(viewport, out var data))
        {
            return;
        }

        _ = SDL.RenderPresent(data.renderer);
    }

    private static bool TryGetViewportData(ImGuiViewport* viewport, out ViewportWindowData data)
    {
        if (viewport == null || viewport->PlatformUserData == null)
        {
            data = null!;
            return false;
        }

        var handle = GCHandle.FromIntPtr((IntPtr)viewport->PlatformUserData);
        if (!handle.IsAllocated || handle.Target is not ViewportWindowData windowData)
        {
            data = null!;
            return false;
        }

        data = windowData;
        return true;
    }

    private static bool TryGetWindow(ImGuiViewport* viewport, out SDLWindowPtr window)
    {
        if (viewport != null && viewport->PlatformHandle != null)
        {
            window = (SDLWindowPtr)(SDLWindow*)viewport->PlatformHandle;
            return true;
        }

        if (TryGetViewportData(viewport, out var data))
        {
            window = data.window;
            return true;
        }

        window = SDLWindowPtr.Null;
        return false;
    }

    private static void RefreshMonitors(SDLWindowPtr fallbackWindow)
    {
        var platformIo = ImGuiNative.GetPlatformIO();
        platformIo.Monitors.Clear();

        var displayCount = 0;
        var displays = SDL.GetDisplays(ref displayCount);
        try
        {
            var primaryDisplay = SDL.GetPrimaryDisplay();
            if (displays != null && displayCount > 0)
            {
                if (primaryDisplay != 0)
                {
                    AddDisplayMonitor(primaryDisplay, ref platformIo);
                }

                for (var i = 0; i < displayCount; i++)
                {
                    var displayId = displays[i];
                    if (displayId == 0 || displayId == primaryDisplay)
                    {
                        continue;
                    }

                    AddDisplayMonitor(displayId, ref platformIo);
                }
            }

            if (platformIo.Monitors.Size == 0 && !fallbackWindow.IsNull)
            {
                var fallbackDisplay = SDL.GetDisplayForWindow(fallbackWindow);
                if (fallbackDisplay != 0)
                {
                    AddDisplayMonitor(fallbackDisplay, ref platformIo);
                }
            }

            if (platformIo.Monitors.Size == 0)
            {
                platformIo.Monitors.PushBack(new ImGuiPlatformMonitor(
                    mainPos: Vector2.Zero,
                    mainSize: new Vector2(1920f, 1080f),
                    workPos: Vector2.Zero,
                    workSize: new Vector2(1920f, 1080f),
                    dpiScale: 1f,
                    platformHandle: null));
            }
        }
        finally
        {
            if (displays != null)
            {
                SDL.Free(displays);
            }
        }
    }

    private static void AddDisplayMonitor(uint displayId, ref ImGuiPlatformIOPtr platformIo)
    {
        SDLRect mainBounds = default;
        if (!SDL.GetDisplayBounds(displayId, ref mainBounds))
        {
            return;
        }

        var workBounds = mainBounds;
        _ = SDL.GetDisplayUsableBounds(displayId, ref workBounds);
        var dpiScale = SDL.GetDisplayContentScale(displayId);
        if (dpiScale <= 0f)
        {
            dpiScale = 1f;
        }

        platformIo.Monitors.PushBack(new ImGuiPlatformMonitor(
            mainPos: new Vector2(mainBounds.X, mainBounds.Y),
            mainSize: new Vector2(mainBounds.W, mainBounds.H),
            workPos: new Vector2(workBounds.X, workBounds.Y),
            workSize: new Vector2(workBounds.W, workBounds.H),
            dpiScale: dpiScale,
            platformHandle: (void*)(nuint)displayId));
    }

    private static void DestroyViewportWindow(ViewportWindowData data)
    {
        if (!data.fontTexture.IsNull)
        {
            SDL.DestroyTexture(data.fontTexture);
            data.fontTexture = SDLTexturePtr.Null;
        }

        if (!data.renderer.IsNull)
        {
            SDL.DestroyRenderer(data.renderer);
            data.renderer = SDLRendererPtr.Null;
        }

        if (!data.window.IsNull)
        {
            SDL.DestroyWindow(data.window);
            data.window = SDLWindowPtr.Null;
        }

        if (data.gcHandle.IsAllocated)
        {
            data.gcHandle.Free();
        }
    }

    private static void EnsureViewportFontTexture(ViewportWindowData data)
    {
        var io = ImGuiNative.GetIO();
        if (io.Fonts.IsNull)
        {
            return;
        }

        io.Fonts.RendererHasTextures = true;
        var texData = io.Fonts.TexData;
        if (texData.IsNull)
        {
            return;
        }

        if (texData.Status == ImTextureStatus.WantDestroy)
        {
            if (!data.fontTexture.IsNull)
            {
                SDL.DestroyTexture(data.fontTexture);
                data.fontTexture = SDLTexturePtr.Null;
                data.fontTextureWidth = 0;
                data.fontTextureHeight = 0;
                data.fontPixelsPtr = 0;
                data.fontTexturePitch = 0;
                data.fontTextureUniqueId = -1;
                data.fontUsedRect = default;
                data.fontUpdateRect = default;
            }

            return;
        }

        if (texData.Pixels == null || texData.Width <= 0 || texData.Height <= 0)
        {
            return;
        }

        var needsRecreate = data.fontTexture.IsNull
            || data.fontTextureWidth != texData.Width
            || data.fontTextureHeight != texData.Height;
        if (needsRecreate)
        {
            if (!data.fontTexture.IsNull)
            {
                SDL.DestroyTexture(data.fontTexture);
                data.fontTexture = SDLTexturePtr.Null;
            }

            data.fontTexture = CreateTexture(data.renderer, texData.Width, texData.Height);
            if (data.fontTexture.IsNull)
            {
                data.fontTextureWidth = 0;
                data.fontTextureHeight = 0;
                return;
            }

            data.fontTextureWidth = texData.Width;
            data.fontTextureHeight = texData.Height;
        }

        var pitch = texData.GetPitch();
        var pixelsPtr = (nint)texData.Pixels;
        var uniqueId = texData.UniqueID;
        var usedRect = texData.UsedRect;
        var updateRect = texData.UpdateRect;
        var needsUpload = needsRecreate
            || data.fontPixelsPtr != pixelsPtr
            || data.fontTexturePitch != pitch
            || data.fontTextureUniqueId != uniqueId
            || !TextureRectEquals(data.fontUsedRect, usedRect)
            || !TextureRectEquals(data.fontUpdateRect, updateRect)
            || texData.Status == ImTextureStatus.WantCreate
            || texData.Status == ImTextureStatus.WantUpdates;
        if (!needsUpload)
        {
            return;
        }

        _ = SDL.UpdateTexture(data.fontTexture, SDLRectPtr.Null, texData.Pixels, pitch);
        data.fontPixelsPtr = pixelsPtr;
        data.fontTexturePitch = pitch;
        data.fontTextureUniqueId = uniqueId;
        data.fontUsedRect = usedRect;
        data.fontUpdateRect = updateRect;
    }

    private static void RenderDrawData(ViewportWindowData data, SDLTexturePtr fontTexture, ImDrawData* drawDataNative)
    {
        var renderer = data.renderer;
        if (renderer.IsNull || drawDataNative == null || drawDataNative->Valid == 0)
        {
            return;
        }

        var drawData = new ImDrawDataPtr(drawDataNative);
        _ = SDL.SetRenderViewport(renderer, SDLRectPtr.Null);
        _ = SDL.SetRenderClipRect(renderer, SDLRectPtr.Null);
        _ = SDL.SetRenderDrawColor(renderer, 0, 0, 0, 0);
        _ = SDL.RenderClear(renderer);

        var clipOff = drawData.DisplayPos;
        var clipScale = drawData.FramebufferScale;
        var fontTexId = ImGuiNative.GetIO().Fonts.TexRef.GetTexID();

        for (var listIndex = 0; listIndex < drawData.CmdListsCount; listIndex++)
        {
            var drawList = drawData.CmdLists[listIndex];
            if (drawList.IsNull || drawList.VtxBuffer.Size <= 0)
            {
                continue;
            }

            var vertexCount = drawList.VtxBuffer.Size;
            EnsureVertexCapacity(data, vertexCount);
            var srcVertices = drawList.VtxBuffer.Data;
            for (var i = 0; i < vertexCount; i++)
            {
                data.vertexScratch[i] = ToSdlVertex(srcVertices[i], clipOff, clipScale);
            }

            var cmdCount = drawList.CmdBuffer.Size;
            for (var cmdIndex = 0; cmdIndex < cmdCount; cmdIndex++)
            {
                var drawCmd = drawList.CmdBuffer[cmdIndex];
                if (drawCmd.UserCallback != null || drawCmd.ElemCount == 0)
                {
                    continue;
                }

                var clipRectX = (drawCmd.ClipRect.X - clipOff.X) * clipScale.X;
                var clipRectY = (drawCmd.ClipRect.Y - clipOff.Y) * clipScale.Y;
                var clipRectZ = (drawCmd.ClipRect.Z - clipOff.X) * clipScale.X;
                var clipRectW = (drawCmd.ClipRect.W - clipOff.Y) * clipScale.Y;
                if (clipRectZ <= clipRectX || clipRectW <= clipRectY)
                {
                    continue;
                }

                var clipRect = new SDLRect((int)clipRectX, (int)clipRectY, (int)(clipRectZ - clipRectX), (int)(clipRectW - clipRectY));
                _ = SDL.SetRenderClipRect(renderer, clipRect);

                var elemCount = (int)drawCmd.ElemCount;
                EnsureIndexCapacity(data, elemCount);
                var srcIndices = drawList.IdxBuffer.Data;
                var idxOffset = (int)drawCmd.IdxOffset;
                var vtxOffset = (int)drawCmd.VtxOffset;
                for (var i = 0; i < elemCount; i++)
                {
                    data.indexScratch[i] = srcIndices[idxOffset + i] + vtxOffset;
                }

                var texture = drawCmd.GetTexID() == fontTexId ? fontTexture : TextureFromImGui(drawCmd.GetTexID());
                if (texture.IsNull)
                {
                    texture = fontTexture;
                }

                fixed (SDLVertex* pVertices = data.vertexScratch)
                fixed (int* pIndices = data.indexScratch)
                {
                    _ = SDL.RenderGeometry(renderer, texture, pVertices, vertexCount, pIndices, elemCount);
                }
            }
        }

        _ = SDL.SetRenderClipRect(renderer, SDLRectPtr.Null);
    }

    private static void FocusWindow(SDLWindowPtr window)
    {
        if (!window.IsNull)
        {
            _ = SDL.RaiseWindow(window);
        }
    }

    private SDLWindowPtr ResolvePointerFocusTarget(uint sourceWindowId)
    {
        SDLWindowPtr mouseFocus = SDL.GetMouseFocus();
        uint mouseFocusWindowId = mouseFocus.IsNull ? 0 : SDL.GetWindowID(mouseFocus);
        if (IsOwnedWindow(mouseFocus)
            && mouseFocusWindowId != sourceWindowId
            && !HasNoInputsViewport(mouseFocusWindowId))
        {
            return mouseFocus;
        }

        float mouseX = 0f;
        float mouseY = 0f;
        _ = SDL.GetGlobalMouseState(ref mouseX, ref mouseY);
        Vector2 mousePosition = new(mouseX, mouseY);

        SDLWindowPtr targetWindow = SDLWindowPtr.Null;
        ImGuiPlatformIOPtr platformIo = ImGuiNative.GetPlatformIO();
        if (platformIo.Viewports.Data == null)
        {
            return targetWindow;
        }

        for (var i = 0; i < platformIo.Viewports.Size; i++)
        {
            ImGuiViewportPtr viewport = platformIo.Viewports[i];
            if (viewport.IsNull
                || (viewport.Flags & ImGuiViewportFlags.NoInputs) != 0
                || !TryGetWindow(viewport.Handle, out SDLWindowPtr candidateWindow))
            {
                continue;
            }

            uint candidateWindowId = SDL.GetWindowID(candidateWindow);
            if (candidateWindowId != 0
                && candidateWindowId != sourceWindowId
                && ContainsPoint(candidateWindow, mousePosition))
            {
                targetWindow = candidateWindow;
            }
        }

        return targetWindow;
    }

    private static bool HasNoInputsViewport(uint windowId)
    {
        if (!s_windowToViewport.TryGetValue(windowId, out uint viewportId))
        {
            return false;
        }

        ImGuiViewportPtr viewport = FindViewportById(viewportId);
        return !viewport.IsNull && (viewport.Flags & ImGuiViewportFlags.NoInputs) != 0;
    }

    private bool IsOwnedWindow(SDLWindowPtr window)
    {
        if (window.IsNull)
        {
            return false;
        }

        uint windowId = SDL.GetWindowID(window);
        return windowId == SDL.GetWindowID(m_mainWindow) || s_windowToViewport.ContainsKey(windowId);
    }

    private static bool ContainsPoint(SDLWindowPtr window, Vector2 point)
    {
        SDLWindowFlags flags = (SDLWindowFlags)SDL.GetWindowFlags(window);
        if ((flags & (SDLWindowFlags.Hidden | SDLWindowFlags.Minimized)) != 0)
        {
            return false;
        }

        var x = 0;
        var y = 0;
        var width = 0;
        var height = 0;
        _ = SDL.GetWindowPosition(window, ref x, ref y);
        SDL.GetWindowSize(window, ref width, ref height);
        return point.X >= x && point.Y >= y && point.X < x + width && point.Y < y + height;
    }

    private static void SynchronizeRendererOutput(SDLRendererPtr renderer)
    {
        if (renderer.IsNull)
        {
            return;
        }

        _ = SDL.SetRenderLogicalPresentation(
            renderer,
            0,
            0,
            SDLRendererLogicalPresentation.Disabled);
        _ = SDL.SetRenderViewport(renderer, SDLRectPtr.Null);
        _ = SDL.SetRenderClipRect(renderer, SDLRectPtr.Null);
    }

    private static void EnsureVertexCapacity(ViewportWindowData data, int required)
    {
        if (data.vertexScratch.Length >= required)
        {
            return;
        }

        Array.Resize(ref data.vertexScratch, required);
    }

    private static void EnsureIndexCapacity(ViewportWindowData data, int required)
    {
        if (data.indexScratch.Length >= required)
        {
            return;
        }

        Array.Resize(ref data.indexScratch, required);
    }

    private static SDLTexturePtr CreateTexture(SDLRendererPtr renderer, int width, int height)
    {
        var props = SDL.CreateProperties();
        try
        {
            _ = SDL.SetNumberProperty(props, SDL.SDL_PROP_TEXTURE_CREATE_FORMAT_NUMBER, (long)SDLPixelFormat.Rgba32);
            _ = SDL.SetNumberProperty(props, SDL.SDL_PROP_TEXTURE_CREATE_ACCESS_NUMBER, (long)SDLTextureAccess.Static);
            _ = SDL.SetNumberProperty(props, SDL.SDL_PROP_TEXTURE_CREATE_WIDTH_NUMBER, width);
            _ = SDL.SetNumberProperty(props, SDL.SDL_PROP_TEXTURE_CREATE_HEIGHT_NUMBER, height);
            var texture = SDL.CreateTextureWithProperties(renderer, props);
            if (!texture.IsNull)
            {
                _ = SDL.SetTextureBlendMode(texture, (uint)SDLBlendMode.Blend);
                _ = SDL.SetTextureScaleMode(texture, SDLScaleMode.Linear);
            }

            return texture;
        }
        finally
        {
            SDL.DestroyProperties(props);
        }
    }

    private static Vector2 GetWindowFramebufferScale(SDLWindowPtr window)
    {
        var windowWidth = 0;
        var windowHeight = 0;
        SDL.GetWindowSize(window, ref windowWidth, ref windowHeight);
        if (windowWidth <= 0 || windowHeight <= 0)
        {
            return Vector2.One;
        }

        var pixelWidth = 0;
        var pixelHeight = 0;
        SDL.GetWindowSizeInPixels(window, ref pixelWidth, ref pixelHeight);
        return new Vector2(pixelWidth / (float)windowWidth, pixelHeight / (float)windowHeight);
    }

    private static float GetWindowDpiScale(SDLWindowPtr window)
    {
        var scale = GetWindowFramebufferScale(window);
        return Math.Max(scale.X, scale.Y);
    }

    private static ImGuiViewportPtr FindViewportById(uint viewportId)
    {
        var platformIo = ImGuiNative.GetPlatformIO();
        for (var i = 0; i < platformIo.Viewports.Size; i++)
        {
            var viewport = platformIo.Viewports[i];
            if (!viewport.IsNull && viewport.ID == viewportId)
            {
                return viewport;
            }
        }

        return ImGuiViewportPtr.Null;
    }

    private static SDLVertex ToSdlVertex(ImDrawVert vtx, Vector2 displayPos, Vector2 framebufferScale)
    {
        var color = ImGuiPackedColor.ToSdlFColor(vtx.Col);
        var x = (vtx.Pos.X - displayPos.X) * framebufferScale.X;
        var y = (vtx.Pos.Y - displayPos.Y) * framebufferScale.Y;

        return new SDLVertex(
            new SDLFPoint(x, y),
            color,
            new SDLFPoint(vtx.Uv.X, vtx.Uv.Y));
    }

    private static SDLTexturePtr TextureFromImGui(ImTextureID textureId)
    {
        return (SDLTexturePtr)(SDLTexture*)(void*)textureId;
    }

    private static bool TextureRectEquals(ImTextureRect a, ImTextureRect b)
    {
        return a.X == b.X
            && a.Y == b.Y
            && a.W == b.W
            && a.H == b.H;
    }

    private static void* FunctionPtr(Delegate del)
    {
        return (void*)Marshal.GetFunctionPointerForDelegate(del);
    }
}
