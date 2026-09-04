using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Inno.Native.Sdl3;

namespace Inno.Platform.Sdl3.ImGui;

/// <summary>
/// Extension APIs that add ImGui integration on top of <see cref="Sdl3PlatformApplication"/>.
/// </summary>
public static class Sdl3PlatformApplicationImGuiExtensions
{
    private sealed class ImGuiState : ISdl3ApplicationExtension
    {
        private readonly Sdl3PlatformApplication m_application;
        private IDisposable? m_registration;

        internal ImGuiState(Sdl3PlatformApplication application)
        {
            m_application = application;
        }

        internal Dictionary<uint, PlatformImGuiContext> contexts { get; } = [];

        internal void Register()
        {
            m_registration = m_application.RegisterExtension(this);
        }

        void ISdl3ApplicationExtension.ProcessNativeEvent(
            Sdl3PlatformApplication application,
            scoped ReadOnlySpan<byte> nativeEventData)
        {
            if (nativeEventData.Length < Marshal.SizeOf<SDLEvent>())
            {
                return;
            }

            SDLEvent sdlEvent = MemoryMarshal.Read<SDLEvent>(nativeEventData);
            foreach (PlatformImGuiContext context in contexts.Values)
                context.ProcessEvent(ref sdlEvent);
        }

        void ISdl3ApplicationExtension.RenderLiveResizeWindow(
            Sdl3PlatformApplication application,
            uint windowId)
        {
            foreach (PlatformImGuiContext context in contexts.Values)
                context.RenderLiveResizeWindow(windowId);
        }

        void ISdl3ApplicationExtension.OnApplicationDisposing(Sdl3PlatformApplication application)
        {
            foreach (PlatformImGuiContext context in contexts.Values)
                context.Dispose();
            contexts.Clear();
            s_states.Remove(application);
            m_registration?.Dispose();
            m_registration = null;
        }
    }

    private static readonly ConditionalWeakTable<Sdl3PlatformApplication, ImGuiState> s_states = new();

    /// <summary>
    /// Creates or returns an existing ImGui context bound to the provided platform window.
    /// </summary>
    /// <param name="application">
    /// Target platform application instance.
    /// </param>
    /// <param name="window">
    /// Target platform window.
    /// </param>
    /// <param name="contextFlags">
    /// ImGui context feature flags.
    /// </param>
    /// <param name="renderer">
    /// Optional presentation backend; the SDL renderer is used when omitted.
    /// </param>
    /// <returns>
    /// The created or existing <see cref="PlatformImGuiContext"/>.
    /// </returns>
    public static PlatformImGuiContext CreateImGuiContext(
        this Sdl3PlatformApplication application,
        Sdl3PlatformWindow window,
        ImGuiContextFlags contextFlags,
        IPlatformImGuiRenderer? renderer = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(window);

        if (window.isClosed)
            throw new InvalidOperationException("Cannot create an ImGui context for a closed window.");

        ImGuiState state = s_states.GetValue(application, CreateState);
        if (state.contexts.TryGetValue(window.windowId, out PlatformImGuiContext? existing))
            return existing;

        var context = new PlatformImGuiContext(window, contextFlags, renderer);
        state.contexts[window.windowId] = context;
        return context;
    }

    /// <summary>
    /// Destroys the ImGui context associated with the provided window, if it exists.
    /// </summary>
    /// <param name="application">
    /// Target platform application instance.
    /// </param>
    /// <param name="window">
    /// Target platform window.
    /// </param>
    public static void DestroyImGuiContext(this Sdl3PlatformApplication application, Sdl3PlatformWindow window)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(window);

        if (!s_states.TryGetValue(application, out ImGuiState? state))
            return;
        if (!state.contexts.Remove(window.windowId, out PlatformImGuiContext? context))
            return;
        context.Dispose();
    }

    private static ImGuiState CreateState(Sdl3PlatformApplication application)
    {
        var state = new ImGuiState(application);
        state.Register();
        return state;
    }
}
