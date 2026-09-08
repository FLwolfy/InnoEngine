using System;
using System.Numerics;
using Inno.Rendering;

namespace Inno.Adapter.Presentation;

/// <summary>
/// Defines the backend-neutral lifecycle used by a graphical composition host.
/// </summary>
public interface IPresentationContext : IRenderFrameGraphContributor, IDisposable
{
    /// <summary>
    /// Disables or redirects backend-owned layout-file persistence.
    /// </summary>
    /// <param name="filePath">
    /// Layout path, or <see langword="null"/> when the host persists layout text itself.
    /// </param>
    void SetLayoutFile(string? filePath);

    /// <summary>
    /// Loads complete layout text captured by the host.
    /// </summary>
    /// <param name="settings">
    /// Layout text, or an empty value when no layout exists.
    /// </param>
    void LoadLayout(string? settings);

    /// <summary>
    /// Captures complete layout text when persistence is required.
    /// </summary>
    /// <param name="settings">
    /// Complete layout text when capture succeeds.
    /// </param>
    /// <param name="force">
    /// Whether to capture even when the backend did not mark the layout dirty.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when layout text was captured.
    /// </returns>
    bool TryCaptureLayout(out string settings, bool force = false);

    /// <summary>
    /// Builds one presentation frame by invoking the host draw callback.
    /// </summary>
    /// <param name="drawFrame">
    /// Callback that emits presentation commands for the current frame.
    /// </param>
    void RenderFrame(Action drawFrame);

    /// <summary>
    /// Registers a persistent render texture for presentation drawing.
    /// </summary>
    /// <param name="texture">
    /// Texture owned by the active rendering-device generation.
    /// </param>
    /// <returns>
    /// An opaque presentation token.
    /// </returns>
    PresentationTextureHandle RegisterTexture(PersistentTextureHandle texture);

    /// <summary>
    /// Releases a presentation token without taking ownership of the source texture.
    /// </summary>
    /// <param name="texture">
    /// Previously allocated presentation token.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the token was registered and released.
    /// </returns>
    bool UnregisterTexture(PresentationTextureHandle texture);

    /// <summary>
    /// Draws a registered texture in the active presentation surface.
    /// </summary>
    /// <param name="texture">
    /// Opaque presentation texture token.
    /// </param>
    /// <param name="size">
    /// Destination size in logical presentation units.
    /// </param>
    void DrawImage(PresentationTextureHandle texture, Vector2 size);

}
