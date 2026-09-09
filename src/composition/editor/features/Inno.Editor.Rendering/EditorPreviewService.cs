using System;
using System.Numerics;
using Inno.Rendering;

namespace Inno.Editor.Rendering;

/// <summary>Identifies one preview texture owned by a specific rendering-device generation.</summary>
public readonly record struct EditorPreviewHandle
{
    /// <summary>Creates one generation-scoped preview handle.</summary>
    /// <param name="value">Non-zero preview identity.</param>
    /// <param name="deviceGeneration">Non-zero rendering-device generation.</param>
    /// <param name="pixelWidth">Positive source width.</param>
    /// <param name="pixelHeight">Positive source height.</param>
    public EditorPreviewHandle(ulong value, uint deviceGeneration, int pixelWidth, int pixelHeight)
    {
        if (value == 0 || deviceGeneration == 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Preview identity and device generation must be non-zero.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        this.value = value;
        this.deviceGeneration = deviceGeneration;
        this.pixelWidth = pixelWidth;
        this.pixelHeight = pixelHeight;
    }

    /// <summary>Gets the opaque preview identity.</summary>
    public ulong value { get; }

    /// <summary>Gets the rendering-device generation that owns this handle.</summary>
    public uint deviceGeneration { get; }

    /// <summary>Gets source width in pixels.</summary>
    public int pixelWidth { get; }

    /// <summary>Gets source height in pixels.</summary>
    public int pixelHeight { get; }

    /// <summary>Gets whether this handle identifies a usable generation-scoped preview.</summary>
    public bool isValid => value != 0 && deviceGeneration != 0 && pixelWidth > 0 && pixelHeight > 0;
}

/// <summary>Provides one shared preview texture bridge for browsers, inspectors, and document canvases.</summary>
public interface IEditorPreviewService
{
    /// <summary>Gets the active rendering-device generation.</summary>
    uint deviceGeneration { get; }

    /// <summary>Tries to resolve a standalone texture preview without blocking target compilation.</summary>
    /// <param name="texture">Texture asset to preview.</param>
    /// <param name="handle">Receives a generation-scoped handle when ready.</param>
    /// <returns><see langword="true"/> when the preview is ready.</returns>
    bool TryGetTexture(TextureAsset texture, out EditorPreviewHandle handle);

    /// <summary>Tries to resolve a named texture artifact preview without blocking target compilation.</summary>
    /// <param name="texture">Stable named texture artifact reference.</param>
    /// <param name="pixelWidth">Positive source width.</param>
    /// <param name="pixelHeight">Positive source height.</param>
    /// <param name="handle">Receives a generation-scoped handle when ready.</param>
    /// <returns><see langword="true"/> when the preview is ready.</returns>
    bool TryGetTextureArtifact(
        RenderTextureArtifactReference texture,
        int pixelWidth,
        int pixelHeight,
        out EditorPreviewHandle handle);

    /// <summary>Draws one current-generation preview into the active presentation surface.</summary>
    /// <param name="handle">Preview handle returned by this service.</param>
    /// <param name="logicalSize">Positive destination size in logical pixels.</param>
    void Draw(EditorPreviewHandle handle, Vector2 logicalSize);

    /// <summary>Releases one cached preview registration.</summary>
    /// <param name="handle">Preview handle returned by this service.</param>
    /// <returns><see langword="true"/> when the handle was active and released.</returns>
    bool Release(EditorPreviewHandle handle);

    /// <summary>Releases every cached preview registration.</summary>
    void ReleaseAll();
}
