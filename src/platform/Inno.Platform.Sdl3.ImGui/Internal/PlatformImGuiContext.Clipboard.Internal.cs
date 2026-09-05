using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Inno.Native.ImGui;
using Inno.Native.Sdl3;
using ImGuiNative = Inno.Native.ImGui.ImGui;

namespace Inno.Platform.Sdl3.ImGui;

/// <summary>
/// Routes Dear ImGui clipboard operations through the SDL platform backend owned by this context.
/// </summary>
public sealed unsafe partial class PlatformImGuiContext
{
    private static readonly PlatformGetClipboardTextFn S_PLATFORM_GET_CLIPBOARD_TEXT = PlatformGetClipboardTextCallback;
    private static readonly PlatformSetClipboardTextFn S_PLATFORM_SET_CLIPBOARD_TEXT = PlatformSetClipboardTextCallback;
    private static readonly object S_CLIPBOARD_CONTEXT_SYNC = new();
    private static readonly Dictionary<nuint, PlatformImGuiContext> S_CLIPBOARD_CONTEXTS = [];

    private nint m_clipboardText;

    private static byte* PlatformGetClipboardTextCallback(ImGuiContext* context)
    {
        try
        {
            PlatformImGuiContext? owner = FindClipboardContext(context);
            return owner is null ? null : owner.GetClipboardText();
        }
        catch
        {
            return null;
        }
    }

    private static void PlatformSetClipboardTextCallback(ImGuiContext* context, byte* text)
    {
        try
        {
            if (text != null && FindClipboardContext(context) is not null)
            {
                _ = SDL.SetClipboardText(text);
            }
        }
        catch
        {
            // Exceptions must never cross Dear ImGui's unmanaged callback boundary.
        }
    }

    private static PlatformImGuiContext? FindClipboardContext(ImGuiContext* context)
    {
        if (context == null)
        {
            return null;
        }

        lock (S_CLIPBOARD_CONTEXT_SYNC)
        {
            return S_CLIPBOARD_CONTEXTS.GetValueOrDefault((nuint)context);
        }
    }

    private byte* GetClipboardText()
    {
        ReleaseClipboardText();
        if (!SDL.HasClipboardText())
        {
            return null;
        }

        byte* text = SDL.GetClipboardText();
        m_clipboardText = (nint)text;
        return text;
    }

    private void RegisterClipboardCallbacks()
    {
        nuint contextKey = (nuint)m_context.Handle;
        lock (S_CLIPBOARD_CONTEXT_SYNC)
        {
            S_CLIPBOARD_CONTEXTS.Add(contextKey, this);
        }

        ImGuiPlatformIOPtr platformIo = ImGuiNative.GetPlatformIO();
        platformIo.PlatformGetClipboardTextFn = FunctionPointer(S_PLATFORM_GET_CLIPBOARD_TEXT);
        platformIo.PlatformSetClipboardTextFn = FunctionPointer(S_PLATFORM_SET_CLIPBOARD_TEXT);
    }

    private void UnregisterClipboardCallbacks()
    {
        ImGuiPlatformIOPtr platformIo = ImGuiNative.GetPlatformIO();
        platformIo.PlatformGetClipboardTextFn = null;
        platformIo.PlatformSetClipboardTextFn = null;

        nuint contextKey = (nuint)m_context.Handle;
        lock (S_CLIPBOARD_CONTEXT_SYNC)
        {
            if (S_CLIPBOARD_CONTEXTS.GetValueOrDefault(contextKey) == this)
            {
                S_CLIPBOARD_CONTEXTS.Remove(contextKey);
            }
        }

        ReleaseClipboardText();
    }

    private void ReleaseClipboardText()
    {
        if (m_clipboardText == 0)
        {
            return;
        }

        SDL.Free(m_clipboardText);
        m_clipboardText = 0;
    }

    private static void* FunctionPointer(Delegate callback)
    {
        return (void*)Marshal.GetFunctionPointerForDelegate(callback);
    }
}
