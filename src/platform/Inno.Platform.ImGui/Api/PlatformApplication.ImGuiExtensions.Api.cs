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
    private sealed unsafe class ImGuiState : IPlatformApplicationExtension
    {
        private readonly PlatformApplication m_application;
        private IDisposable? m_registration;

        internal ImGuiState(PlatformApplication application)
        {
            m_application = application;
        }

        internal Dictionary<uint, PlatformImGuiContext> contexts { get; } = [];

        internal void Register()
        {
            m_registration = m_application.RegisterExtension(this);
        }

        void IPlatformApplicationExtension.ProcessNativeEvent(
            PlatformApplication application,
            PlatformNativeEvent nativeEvent)
        {
            if (!string.Equals(nativeEvent.backendName, "SDL3", StringComparison.Ordinal) ||
                nativeEvent.data == IntPtr.Zero)
            {
                return;
            }
            ref SDLEvent sdlEvent = ref Unsafe.AsRef<SDLEvent>(nativeEvent.data.ToPointer());
            foreach (PlatformImGuiContext context in contexts.Values)
                context.ProcessEvent(ref sdlEvent);
        }

        void IPlatformApplicationExtension.RenderLiveResizeWindow(
            PlatformApplication application,
            uint windowId)
        {
            foreach (PlatformImGuiContext context in contexts.Values)
                context.RenderLiveResizeWindow(windowId);
        }

        void IPlatformApplicationExtension.OnApplicationDisposing(PlatformApplication application)
        {
            foreach (PlatformImGuiContext context in contexts.Values)
                context.Dispose();
            contexts.Clear();
            s_states.Remove(application);
            m_registration?.Dispose();
            m_registration = null;
        }
    }

    private static readonly ConditionalWeakTable<PlatformApplication, ImGuiState> s_states = new();

    /// <summary>
    /// Creates or returns an existing ImGui context bound to the provided platform window.
    /// </summary>
    /// <param name="application">Target platform application instance.</param>
    /// <param name="window">Target platform window.</param>
    /// <param name="contextFlags">ImGui context feature flags.</param>
    /// <param name="renderer">Optional presentation backend; the SDL renderer is used when omitted.</param>
    /// <returns>The created or existing <see cref="PlatformImGuiContext"/>.</returns>
    public static PlatformImGuiContext CreateImGuiContext(
        this PlatformApplication application,
        PlatformWindow window,
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
    /// <param name="application">Target platform application instance.</param>
    /// <param name="window">Target platform window.</param>
    public static void DestroyImGuiContext(this PlatformApplication application, PlatformWindow window)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(window);

        if (!s_states.TryGetValue(application, out ImGuiState? state))
            return;
        if (!state.contexts.Remove(window.windowId, out PlatformImGuiContext? context))
            return;
        context.Dispose();
    }

    private static ImGuiState CreateState(PlatformApplication application)
    {
        var state = new ImGuiState(application);
        state.Register();
        return state;
    }
}
