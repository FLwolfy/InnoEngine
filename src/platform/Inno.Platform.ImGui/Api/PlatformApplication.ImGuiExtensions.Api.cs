using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Inno.Native.SDL3;

namespace Inno.Platform.ImGui;

/// <summary>
/// Extension APIs that add ImGui integration on top of <see cref="PlatformApplication"/>.
/// </summary>
public static class PlatformApplicationImGuiExtensions
{
    private sealed class ImGuiState
    {
        internal readonly Dictionary<uint, PlatformImGuiContext> contexts = [];
    }

    private static readonly ConditionalWeakTable<PlatformApplication, ImGuiState> s_states = new();
    private static readonly object s_hookLock = new();
    private static bool s_hooksInstalled;

    /// <summary>
    /// Creates or returns an existing ImGui context bound to the provided platform window.
    /// </summary>
    /// <param name="application">Target platform application instance.</param>
    /// <param name="window">Target platform window.</param>
    /// <param name="enableViewports">Whether to enable ImGui multi-viewport support.</param>
    /// <param name="enableDocking">Whether to enable ImGui docking support.</param>
    /// <returns>The created or existing <see cref="PlatformImGuiContext"/>.</returns>
    public static PlatformImGuiContext CreateImGuiContext(
        this PlatformApplication application,
        PlatformWindow window,
        bool enableViewports = true,
        bool enableDocking = true)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(window);

        if (window.isClosed)
        {
            throw new InvalidOperationException("Cannot create an ImGui context for a closed window.");
        }

        EnsureHooksInstalled();
        var state = s_states.GetOrCreateValue(application);
        if (state.contexts.TryGetValue(window.windowId, out var existing))
        {
            return existing;
        }

        var context = new PlatformImGuiContext(window, enableViewports, enableDocking);
        state.contexts[window.windowId] = context;
        return context;
    }

    /// <summary>
    /// Destroys the ImGui context associated with the provided window, if it exists.
    /// </summary>
    /// <param name="application">Target platform application instance.</param>
    /// <param name="window">Target platform window.</param>
    public static void DestroyImGuiContext(this PlatformApplication application, PlatformWindow window)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(window);

        if (!s_states.TryGetValue(application, out var state))
        {
            return;
        }

        if (!state.contexts.Remove(window.windowId, out var context))
        {
            return;
        }

        context.Dispose();
    }

    private static void EnsureHooksInstalled()
    {
        if (s_hooksInstalled)
        {
            return;
        }

        lock (s_hookLock)
        {
            if (s_hooksInstalled)
            {
                return;
            }

            PlatformApplicationHooks.s_onSdlEvent += OnSdlEvent;
            PlatformApplicationHooks.s_onLiveResizeRedraw += OnLiveResizeRedraw;
            PlatformApplicationHooks.s_onDisposing += OnApplicationDisposing;
            s_hooksInstalled = true;
        }
    }

    private static void OnSdlEvent(PlatformApplication application, ref SDLEvent sdlEvent)
    {
        if (!s_states.TryGetValue(application, out var state))
        {
            return;
        }

        foreach (var context in state.contexts.Values)
        {
            context.ProcessEvent(ref sdlEvent);
        }
    }

    private static void OnLiveResizeRedraw(PlatformApplication application, uint windowId)
    {
        if (!s_states.TryGetValue(application, out var state))
        {
            return;
        }

        foreach (var context in state.contexts.Values)
        {
            context.RenderLiveResizeWindow(windowId);
        }
    }

    private static void OnApplicationDisposing(PlatformApplication application)
    {
        if (!s_states.TryGetValue(application, out var state))
        {
            return;
        }

        foreach (var context in state.contexts.Values)
        {
            context.Dispose();
        }

        state.contexts.Clear();
        s_states.Remove(application);
    }
}
