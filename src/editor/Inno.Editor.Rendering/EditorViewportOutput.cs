using System;
using System.Collections.Generic;
using System.Numerics;
using Inno.Platform.ImGui;

namespace Inno.Editor.Rendering;

/// <summary>
/// Identifies a renderer-owned editor viewport image through an opaque presentation token.
/// </summary>
public readonly record struct EditorViewportOutput
{
    /// <summary>Creates an editor viewport output snapshot.</summary>
    /// <param name="viewportId">Stable viewport identity.</param>
    /// <param name="texture">Opaque presentation token, or an invalid token while warming up.</param>
    /// <param name="pixelWidth">Current target width.</param>
    /// <param name="pixelHeight">Current target height.</param>
    public EditorViewportOutput(
        string viewportId,
        ImGuiTextureHandle texture,
        int pixelWidth,
        int pixelHeight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewportId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        this.viewportId = viewportId;
        this.texture = texture;
        this.pixelWidth = pixelWidth;
        this.pixelHeight = pixelHeight;
    }

    /// <summary>Gets the stable viewport identity.</summary>
    public string viewportId { get; }

    /// <summary>Gets the opaque presentation token.</summary>
    public ImGuiTextureHandle texture { get; }

    /// <summary>Gets the current target width.</summary>
    public int pixelWidth { get; }

    /// <summary>Gets the current target height.</summary>
    public int pixelHeight { get; }

    /// <summary>Gets whether a completed target can be drawn.</summary>
    public bool isReady => texture.isValid;
}

/// <summary>
/// Bridges editor extensions to a host-owned render request sink and opaque texture presenter.
/// </summary>
public interface IEditorRenderingHost
{
    /// <summary>Gets the active project-relative pipeline asset path, or <see langword="null"/> for host defaults.</summary>
    string? activePipelineAssetPath { get; }

    /// <summary>Enumerates valid pipeline assets currently available in the project.</summary>
    /// <returns>Stable picker data sorted by project-relative path.</returns>
    IReadOnlyList<EditorPipelineAssetInfo> GetPipelineAssets();

    /// <summary>Attempts to activate an imported pipeline and its complete feature generation.</summary>
    /// <param name="assetPath">Project-relative pipeline asset path.</param>
    /// <returns><see langword="true"/> when the candidate became active.</returns>
    bool TryActivatePipelineAsset(string assetPath);

    /// <summary>Submits or updates one offscreen editor viewport.</summary>
    /// <param name="request">Complete frame request.</param>
    /// <returns>The current presentation output, which can be warming up for one frame.</returns>
    EditorViewportOutput Submit(EditorViewportRequest request);

    /// <summary>Draws a ready viewport output inside the current ImGui window.</summary>
    /// <param name="output">Output returned by <see cref="Submit"/>.</param>
    /// <param name="logicalSize">Destination size in logical UI pixels.</param>
    void Draw(EditorViewportOutput output, Vector2 logicalSize);

    /// <summary>Releases one viewport and queues its GPU target for frame-safe destruction.</summary>
    /// <param name="viewportId">Stable viewport identity.</param>
    void Release(string viewportId);

    /// <summary>Releases every viewport owned by this editor host service.</summary>
    void ReleaseAll();
}
