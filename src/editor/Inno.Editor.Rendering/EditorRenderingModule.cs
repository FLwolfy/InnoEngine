using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Core.Scripting;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Rendering;
using EngineColor = Inno.Core.Mathematics.Color;

namespace Inno.Editor.Rendering;

/// <summary>Hosts reloadable viewport providers while retaining only opaque presentation outputs.</summary>
[EditorModule("rendering.viewports", order: 175)]
public sealed class EditorRenderingModule : EditorModule
{
    private readonly Dictionary<string, EditorViewportNavigationState> m_navigationStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RenderContentScope> m_contentScopes = new(StringComparer.Ordinal);
    private readonly Dictionary<EditorViewportKindId, string> m_providerErrors = [];
    private readonly Dictionary<string, EditorViewportManipulationSpace> m_manipulationSpaces =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, EditorViewportPresentation> m_presentations =
        new(StringComparer.Ordinal);
    private readonly IEditorRenderingHost m_host;
    private readonly EditorInteractions m_interactions;
    private readonly EditorViewportProviderRegistry m_providers = new();
    private EditorContext? m_context;

    /// <summary>Creates the provider host around stable rendering and interaction services.</summary>
    /// <param name="host">Host-owned target and opaque texture bridge.</param>
    /// <param name="interactions">Shared Editor interaction and selection service.</param>
    [ScriptingApiIgnore]
    public EditorRenderingModule(IEditorRenderingHost host, EditorInteractions interactions)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
    }

    /// <summary>Gets whether the current extension generation provides one viewport purpose.</summary>
    /// <param name="kind">Open viewport purpose.</param>
    /// <returns><see langword="true"/> when an active provider is available.</returns>
    public bool HasProvider(EditorViewportKindId kind)
        => kind.isValid && m_providers.providers.byKind.ContainsKey(kind);

    /// <summary>Gets the most recent isolated provider failure for a viewport purpose.</summary>
    /// <param name="kind">Open viewport purpose.</param>
    /// <returns>The failure message, or null when no current failure exists.</returns>
    public string? GetProviderError(EditorViewportKindId kind)
        => m_providerErrors.GetValueOrDefault(kind);

    /// <summary>Gets the host-owned neutral navigation state for one stable Editor viewport.</summary>
    /// <param name="viewportId">Stable panel viewport identity.</param>
    /// <returns>The reusable navigation state owned by the Editor host.</returns>
    [ScriptingApiIgnore]
    public EditorViewportNavigationState GetNavigationState(string viewportId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewportId);
        if (!m_navigationStates.TryGetValue(viewportId, out EditorViewportNavigationState? state))
        {
            state = new EditorViewportNavigationState();
            m_navigationStates.Add(viewportId, state);
        }
        return state;
    }

    /// <summary>Sets the explicit ordered host content visible to one Editor viewport.</summary>
    /// <param name="viewportId">Stable panel viewport identity.</param>
    /// <param name="content">Current frame-safe content scope.</param>
    [ScriptingApiIgnore]
    public void SetContentScope(string viewportId, RenderContentScope content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewportId);
        m_contentScopes[viewportId] = content ?? throw new ArgumentNullException(nameof(content));
    }

    /// <summary>Queries the active provider's navigation contract before viewport input is processed.</summary>
    /// <param name="kind">Open viewport purpose.</param>
    /// <param name="viewportId">Stable panel viewport identity.</param>
    /// <param name="pixelWidth">Positive target width.</param>
    /// <param name="pixelHeight">Positive target height.</param>
    /// <param name="profile">Receives the active provider profile.</param>
    /// <returns>True when a provider returned a usable navigation profile.</returns>
    public bool TryConfigureNavigation(
        EditorViewportKindId kind,
        string viewportId,
        int pixelWidth,
        int pixelHeight,
        out EditorViewportNavigationProfile profile)
    {
        profile = EditorViewportNavigationProfile.disabled;
        if (!TryCreateContext(kind, viewportId, pixelWidth, pixelHeight, out EditorViewportContext? context)
            || !m_providers.providers.byKind.TryGetValue(kind, out EditorViewportProviderRegistry.Registration? registration))
        {
            return false;
        }
        try
        {
            profile = registration.provider.ConfigureNavigation(context!)
                ?? throw new InvalidOperationException("Viewport provider returned a null navigation profile.");
            m_providerErrors.Remove(kind);
            return profile.id.isValid;
        }
        catch (Exception exception)
        {
            m_providerErrors[kind] =
                $"Viewport provider '{registration.attribute.id}' navigation failed: {exception.Message}";
            profile = EditorViewportNavigationProfile.disabled;
            return false;
        }
    }

    /// <summary>Sets presentation preferences supplied to the provider for one Editor viewport.</summary>
    /// <param name="viewportId">Stable panel viewport identity.</param>
    /// <param name="presentation">Current host-owned presentation preferences.</param>
    [ScriptingApiIgnore]
    public void SetPresentation(string viewportId, EditorViewportPresentation presentation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewportId);
        m_presentations[viewportId] = presentation;
    }

    /// <summary>Tries to get the manipulation space from the latest accepted submission for one viewport.</summary>
    /// <param name="viewportId">Stable panel viewport identity.</param>
    /// <param name="space">Receives the latest exact view/projection contract.</param>
    /// <returns><see langword="true"/> when the active provider supplied a manipulation space.</returns>
    [ScriptingApiIgnore]
    public bool TryGetManipulationSpace(
        string viewportId,
        out EditorViewportManipulationSpace space)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewportId);
        return m_manipulationSpaces.TryGetValue(viewportId, out space);
    }

    /// <summary>Draws toolbar controls owned by the selected Plugin provider.</summary>
    /// <param name="kind">Open viewport purpose.</param>
    /// <param name="viewportId">Stable panel viewport identity.</param>
    /// <param name="pixelWidth">Current target width.</param>
    /// <param name="pixelHeight">Current target height.</param>
    public void DrawProviderToolbar(
        EditorViewportKindId kind,
        string viewportId,
        int pixelWidth,
        int pixelHeight)
    {
        if (!TryCreateContext(kind, viewportId, pixelWidth, pixelHeight, out EditorViewportContext? context)
            || !m_providers.providers.byKind.TryGetValue(kind, out EditorViewportProviderRegistry.Registration? registration))
        {
            return;
        }
        try
        {
            registration.provider.DrawToolbar(context!);
            m_providerErrors.Remove(kind);
        }
        catch (Exception exception)
        {
            m_providerErrors[kind] =
                $"Viewport provider '{registration.attribute.id}' toolbar failed: {exception.Message}";
        }
    }

    /// <summary>Builds, submits, and returns one provider-owned offscreen viewport.</summary>
    /// <param name="kind">Open viewport purpose.</param>
    /// <param name="viewportId">Stable panel viewport identity.</param>
    /// <param name="pixelWidth">Positive target width.</param>
    /// <param name="pixelHeight">Positive target height.</param>
    /// <param name="output">Receives the opaque viewport output.</param>
    /// <returns><see langword="true"/> when a provider submission was accepted.</returns>
    public bool TrySubmit(
        EditorViewportKindId kind,
        string viewportId,
        int pixelWidth,
        int pixelHeight,
        out EditorViewportOutput output)
    {
        output = default;
        if (!TryCreateContext(kind, viewportId, pixelWidth, pixelHeight, out EditorViewportContext? context)
            || !m_providers.providers.byKind.TryGetValue(kind, out EditorViewportProviderRegistry.Registration? registration))
        {
            m_host.Release(viewportId);
            m_manipulationSpaces.Remove(viewportId);
            return false;
        }
        try
        {
            EditorViewportSubmission submission = registration.provider.Build(context!);
            output = m_host.Submit(new EditorViewportRequest(
                viewportId,
                pixelWidth,
                pixelHeight,
                submission.pipeline,
                submission.data,
                submission.targetFormat,
                submission.priority));
            if (submission.manipulationSpace is EditorViewportManipulationSpace manipulationSpace)
                m_manipulationSpaces[viewportId] = manipulationSpace;
            else
                m_manipulationSpaces.Remove(viewportId);
            m_providerErrors.Remove(kind);
            return true;
        }
        catch (Exception exception)
        {
            m_providerErrors[kind] =
                $"Viewport provider '{registration.attribute.id}' failed: {exception.Message}";
            m_host.Release(viewportId);
            m_manipulationSpaces.Remove(viewportId);
            return false;
        }
    }

    /// <summary>Forwards a normalized click to the selected Plugin provider.</summary>
    /// <param name="kind">Open viewport purpose.</param>
    /// <param name="viewportId">Stable panel viewport identity.</param>
    /// <param name="pixelWidth">Current target width.</param>
    /// <param name="pixelHeight">Current target height.</param>
    /// <param name="x">Normalized horizontal position.</param>
    /// <param name="y">Normalized vertical position.</param>
    /// <param name="button">Platform-independent pointer button index.</param>
    public void HandlePointer(
        EditorViewportKindId kind,
        string viewportId,
        int pixelWidth,
        int pixelHeight,
        float x,
        float y,
        int button)
    {
        if (!TryCreateContext(kind, viewportId, pixelWidth, pixelHeight, out EditorViewportContext? context)
            || !m_providers.providers.byKind.TryGetValue(kind, out EditorViewportProviderRegistry.Registration? registration))
        {
            return;
        }
        try
        {
            registration.provider.HandlePointer(new EditorViewportPointerContext(context!, x, y, button));
            m_providerErrors.Remove(kind);
        }
        catch (Exception exception)
        {
            m_providerErrors[kind] =
                $"Viewport provider '{registration.attribute.id}' pointer handler failed: {exception.Message}";
        }
    }

    /// <summary>Draws a ready output in the current panel.</summary>
    /// <param name="output">Opaque output returned by <see cref="TrySubmit"/>.</param>
    /// <param name="logicalSize">Destination size in logical UI pixels.</param>
    public void Draw(EditorViewportOutput output, Vector2 logicalSize)
        => m_host.Draw(output, logicalSize);

    /// <summary>Stops retaining one viewport target.</summary>
    /// <param name="viewportId">Stable viewport identity.</param>
    public void Release(string viewportId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewportId);
        m_manipulationSpaces.Remove(viewportId);
        m_contentScopes.Remove(viewportId);
        m_host.Release(viewportId);
    }

    /// <inheritdoc />
    protected override void OnStart(EditorContext context)
    {
        m_context = context;
        _ = m_providers.providers;
    }

    /// <inheritdoc />
    protected override void OnStop(EditorContext context)
    {
        _ = context;
        m_context = null;
        m_host.ReleaseAll();
        m_navigationStates.Clear();
        m_contentScopes.Clear();
        m_providerErrors.Clear();
        m_manipulationSpaces.Clear();
        m_presentations.Clear();
    }

    /// <inheritdoc />
    protected override void OnDispose()
    {
        m_providers.Dispose();
        m_navigationStates.Clear();
        m_contentScopes.Clear();
        m_manipulationSpaces.Clear();
        m_presentations.Clear();
        m_host.ReleaseAll();
    }

    private bool TryCreateContext(
        EditorViewportKindId kind,
        string viewportId,
        int pixelWidth,
        int pixelHeight,
        out EditorViewportContext? context)
    {
        if (!kind.isValid || m_context is null)
        {
            context = null;
            return false;
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(viewportId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        context = new EditorViewportContext(
            m_context,
            m_interactions,
            kind,
            viewportId,
            pixelWidth,
            pixelHeight,
            GetNavigationState(viewportId),
            m_contentScopes.GetValueOrDefault(viewportId, RenderContentScope.empty),
            m_presentations.GetValueOrDefault(
                viewportId,
                new EditorViewportPresentation(EngineColor.DARKGRAY)));
        return true;
    }
}
