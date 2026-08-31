using System;
using System.Numerics;

using Inno.Native.SDL3;
using Inno.Native.ImGui;
using ImGuiNative = Inno.Native.ImGui.ImGui;

namespace Inno.Platform.ImGui;

internal sealed unsafe class PlatformImGuiSdlRenderer : IDisposable
{
    private SDLRendererPtr m_renderer;
    private SDLTexturePtr m_fontTexture;
    private SDLVertex[] m_vertexScratch = [];
    private int[] m_indexScratch = [];
    private bool m_disposed;

    internal PlatformImGuiSdlRenderer(PlatformWindow window)
    {
        m_renderer = SDL.CreateRenderer(window.GetSdlWindow(), (byte*)0);
        if (m_renderer.IsNull)
        {
            throw SDL.GetErrorAsException() ?? new InvalidOperationException("SDL_CreateRenderer failed.");
        }

        _ = SDL.SetRenderDrawBlendMode(m_renderer, (uint)SDLBlendMode.Blend);
    }

    internal void Render(ImDrawDataPtr drawData)
    {
        if (m_disposed || m_renderer.IsNull || drawData.IsNull || !drawData.Valid)
        {
            return;
        }

        ProcessTextureRequests(drawData);
        EnsureFontTexture();

        _ = SDL.SetRenderViewport(m_renderer, SDLRectPtr.Null);
        _ = SDL.SetRenderClipRect(m_renderer, SDLRectPtr.Null);
        _ = SDL.SetRenderDrawColor(m_renderer, 20, 20, 22, 255);
        _ = SDL.RenderClear(m_renderer);

        var clipOff = drawData.DisplayPos;
        var clipScale = drawData.FramebufferScale;

        for (var listIndex = 0; listIndex < drawData.CmdListsCount; listIndex++)
        {
            var drawList = drawData.CmdLists[listIndex];
            if (drawList.IsNull)
            {
                continue;
            }

            var vertexCount = drawList.VtxBuffer.Size;
            if (vertexCount <= 0)
            {
                continue;
            }

            EnsureVertexCapacity(vertexCount);
            var srcVertices = drawList.VtxBuffer.Data;
            for (var i = 0; i < vertexCount; i++)
            {
                m_vertexScratch[i] = ToSdlVertex(srcVertices[i], clipOff, clipScale);
            }

            var cmdBufferCount = drawList.CmdBuffer.Size;
            for (var cmdIndex = 0; cmdIndex < cmdBufferCount; cmdIndex++)
            {
                var drawCmd = drawList.CmdBuffer[cmdIndex];
                if (drawCmd.UserCallback != null)
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

                var clipRect = new SDLRect(
                    x: (int)clipRectX,
                    y: (int)clipRectY,
                    w: (int)(clipRectZ - clipRectX),
                    h: (int)(clipRectW - clipRectY));

                if (!SDL.SetRenderClipRect(m_renderer, clipRect))
                {
                    continue;
                }

                var elemCount = (int)drawCmd.ElemCount;
                if (elemCount <= 0)
                {
                    continue;
                }

                EnsureIndexCapacity(elemCount);

                var srcIndices = drawList.IdxBuffer.Data;
                var idxOffset = (int)drawCmd.IdxOffset;
                var vtxOffset = (int)drawCmd.VtxOffset;
                for (var i = 0; i < elemCount; i++)
                {
                    m_indexScratch[i] = srcIndices[idxOffset + i] + vtxOffset;
                }

                var textureId = drawCmd.GetTexID();
                var texture = TextureFromImGui(textureId);
                if (texture.IsNull)
                {
                    texture = m_fontTexture;
                }

                fixed (SDLVertex* pVertices = m_vertexScratch)
                fixed (int* pIndices = m_indexScratch)
                {
                    _ = SDL.RenderGeometry(
                        m_renderer,
                        texture,
                        pVertices,
                        vertexCount,
                        pIndices,
                        elemCount);
                }
            }
        }

        _ = SDL.SetRenderClipRect(m_renderer, SDLRectPtr.Null);
        _ = SDL.RenderPresent(m_renderer);
    }

    /// <summary>
    /// Synchronizes the SDL renderer with the current native drawable size.
    /// </summary>
    internal void SynchronizeOutputSize()
    {
        if (m_disposed || m_renderer.IsNull)
        {
            return;
        }

        // A macOS live-resize expose callback can run before SDL's renderer event watcher has
        // consumed the matching pixel-size event. Re-applying the disabled presentation mode
        // makes SDL query the native drawable immediately instead of clipping to its old size.
        _ = SDL.SetRenderLogicalPresentation(
            m_renderer,
            0,
            0,
            SDLRendererLogicalPresentation.Disabled);
        _ = SDL.SetRenderViewport(m_renderer, SDLRectPtr.Null);
        _ = SDL.SetRenderClipRect(m_renderer, SDLRectPtr.Null);
    }

    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        if (!m_fontTexture.IsNull)
        {
            SDL.DestroyTexture(m_fontTexture);
            m_fontTexture = SDLTexturePtr.Null;
        }

        if (!m_renderer.IsNull)
        {
            SDL.DestroyRenderer(m_renderer);
            m_renderer = SDLRendererPtr.Null;
        }

        m_disposed = true;
    }

    private void ProcessTextureRequests(ImDrawDataPtr drawData)
    {
        var textures = drawData.Handle->Textures;
        if (textures == null)
        {
            return;
        }

        for (var i = 0; i < textures->Size; i++)
        {
            var textureData = textures->Data[i];
            if (textureData.IsNull)
            {
                continue;
            }

            switch (textureData.Status)
            {
                case ImTextureStatus.WantCreate:
                    CreateOrUpdateTexture(textureData, createIfMissing: true);
                    break;
                case ImTextureStatus.WantUpdates:
                    CreateOrUpdateTexture(textureData, createIfMissing: true);
                    break;
                case ImTextureStatus.WantDestroy:
                    DestroyTexture(textureData);
                    break;
            }
        }
    }

    private void EnsureFontTexture()
    {
        var io = ImGuiNative.GetIO();
        var fonts = io.Fonts;
        if (fonts.IsNull)
        {
            return;
        }

        fonts.RendererHasTextures = true;
        var texData = fonts.TexData;
        if (texData.IsNull)
        {
            return;
        }

        switch (texData.Status)
        {
            case ImTextureStatus.WantCreate:
            case ImTextureStatus.WantUpdates:
                CreateOrUpdateTexture(texData, createIfMissing: true);
                break;
            case ImTextureStatus.WantDestroy:
                DestroyTexture(texData);
                break;
        }

        if (m_fontTexture.IsNull)
        {
            m_fontTexture = TextureFromImGui(texData.TexID);
        }
    }

    private void CreateOrUpdateTexture(ImTextureDataPtr textureData, bool createIfMissing)
    {
        var texture = TextureFromImGui(textureData.TexID);
        if (texture.IsNull && createIfMissing)
        {
            texture = CreateTexture(textureData.Width, textureData.Height);
            if (texture.IsNull)
            {
                return;
            }

            textureData.SetTexID((ImTextureID)(void*)texture.Handle);
        }

        if (texture.IsNull || textureData.Pixels == null)
        {
            return;
        }

        var pitch = textureData.GetPitch();
        _ = SDL.UpdateTexture(texture, SDLRectPtr.Null, textureData.Pixels, pitch);
        textureData.SetStatus(ImTextureStatus.Ok);

        var io = ImGuiNative.GetIO();
        if (textureData.TexID == io.Fonts.TexRef.GetTexID())
        {
            m_fontTexture = texture;
        }
    }

    private void DestroyTexture(ImTextureDataPtr textureData)
    {
        var texture = TextureFromImGui(textureData.TexID);
        if (!texture.IsNull)
        {
            SDL.DestroyTexture(texture);
        }

        if (texture == m_fontTexture)
        {
            m_fontTexture = SDLTexturePtr.Null;
        }

        textureData.SetTexID(ImTextureID.Null);
        textureData.SetStatus(ImTextureStatus.Destroyed);
    }

    private SDLTexturePtr CreateTexture(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return SDLTexturePtr.Null;
        }

        var props = SDL.CreateProperties();
        try
        {
            _ = SDL.SetNumberProperty(props, SDL.SDL_PROP_TEXTURE_CREATE_FORMAT_NUMBER, (long)SDLPixelFormat.Rgba32);
            _ = SDL.SetNumberProperty(props, SDL.SDL_PROP_TEXTURE_CREATE_ACCESS_NUMBER, (long)SDLTextureAccess.Static);
            _ = SDL.SetNumberProperty(props, SDL.SDL_PROP_TEXTURE_CREATE_WIDTH_NUMBER, width);
            _ = SDL.SetNumberProperty(props, SDL.SDL_PROP_TEXTURE_CREATE_HEIGHT_NUMBER, height);

            var texture = SDL.CreateTextureWithProperties(m_renderer, props);
            if (texture.IsNull)
            {
                return SDLTexturePtr.Null;
            }

            _ = SDL.SetTextureBlendMode(texture, (uint)SDLBlendMode.Blend);
            _ = SDL.SetTextureScaleMode(texture, SDLScaleMode.Linear);
            return texture;
        }
        finally
        {
            SDL.DestroyProperties(props);
        }
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

    private void EnsureVertexCapacity(int requiredCount)
    {
        if (m_vertexScratch.Length < requiredCount)
        {
            m_vertexScratch = new SDLVertex[Math.Max(requiredCount, m_vertexScratch.Length * 2 + 64)];
        }
    }

    private void EnsureIndexCapacity(int requiredCount)
    {
        if (m_indexScratch.Length < requiredCount)
        {
            m_indexScratch = new int[Math.Max(requiredCount, m_indexScratch.Length * 2 + 256)];
        }
    }
}
