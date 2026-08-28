using System;
using System.Collections.Generic;
using System.Numerics;
using Inno.Editor.Core;

namespace Inno.Editor.Rendering;

/// <summary>
/// Provides reload-safe editor panels with backend-neutral viewport submission and presentation.
/// </summary>
[EditorModule("rendering.viewports", order: 175)]
public sealed class EditorRenderingModule : EditorModule
{
    private readonly IEditorRenderingHost m_host;

    /// <summary>Creates the module around the stable host rendering service.</summary>
    /// <param name="host">Host-owned rendering and opaque presentation bridge.</param>
    public EditorRenderingModule(IEditorRenderingHost host)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>Gets the active project-relative pipeline asset path, or <see langword="null"/> for host defaults.</summary>
    public string? activePipelineAssetPath => m_host.activePipelineAssetPath;

    /// <summary>Enumerates pipeline assets available to the current project.</summary>
    /// <returns>Stable picker data sorted by project-relative path.</returns>
    public IReadOnlyList<EditorPipelineAssetInfo> GetPipelineAssets() => m_host.GetPipelineAssets();

    /// <summary>Attempts to activate a complete pipeline and feature generation from one project asset.</summary>
    /// <param name="assetPath">Project-relative pipeline asset path.</param>
    /// <returns><see langword="true"/> when activation succeeded without replacing last-good state on failure.</returns>
    public bool TryActivatePipelineAsset(string assetPath) => m_host.TryActivatePipelineAsset(assetPath);

    /// <summary>Submits or updates one offscreen viewport for the current editor frame.</summary>
    /// <param name="request">Complete viewport request.</param>
    /// <returns>The current output, which can be warming up for one frame.</returns>
    public EditorViewportOutput Submit(EditorViewportRequest request)
        => m_host.Submit(request ?? throw new ArgumentNullException(nameof(request)));

    /// <summary>Draws a ready output in the current panel.</summary>
    /// <param name="output">Output returned by <see cref="Submit"/>.</param>
    /// <param name="logicalSize">Destination size in logical UI pixels.</param>
    public void Draw(EditorViewportOutput output, Vector2 logicalSize)
        => m_host.Draw(output, logicalSize);

    /// <summary>Stops retaining one viewport target.</summary>
    /// <param name="viewportId">Stable viewport identity.</param>
    public void Release(string viewportId) => m_host.Release(viewportId);

    /// <inheritdoc />
    protected override void OnDispose() => m_host.ReleaseAll();
}
