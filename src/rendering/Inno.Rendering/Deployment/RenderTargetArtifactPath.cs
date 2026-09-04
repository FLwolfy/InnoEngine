using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Inno.Rendering;

/// <summary>
/// Defines deterministic relative paths shared by target-artifact producers and runtime consumers.
/// </summary>
public static class RenderTargetArtifactPath
{
    /// <summary>
    /// Gets the relative deployment path for one target shader variant.
    /// </summary>
    /// <param name="shaderId">
    /// The persistent shader asset identity.
    /// </param>
    /// <param name="backend">
    /// The graphics backend selected by the target Player.
    /// </param>
    /// <param name="variant">
    /// The canonical static keyword selection.
    /// </param>
    /// <returns>
    /// A platform-neutral path beneath the runtime content root.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="shaderId"/> is empty.
    /// </exception>
    public static string GetShaderPath(
        Guid shaderId,
        GraphicsBackend backend,
        RenderShaderVariant variant)
    {
        if (shaderId == Guid.Empty)
            throw new ArgumentException("A target shader path requires a persistent asset identity.", nameof(shaderId));
        return Path.Combine(
            "TargetArtifacts",
            "Shaders",
            backend.ToString(),
            shaderId.ToString("D", CultureInfo.InvariantCulture),
            HashVariant(variant.value) + ".shader");
    }

    /// <summary>
    /// Gets the relative deployment path for one portable texture artifact.
    /// </summary>
    /// <param name="textureId">
    /// The persistent texture asset identity.
    /// </param>
    /// <returns>
    /// A platform-neutral path beneath the runtime content root.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="textureId"/> is empty.
    /// </exception>
    public static string GetTexturePath(Guid textureId)
    {
        if (textureId == Guid.Empty)
            throw new ArgumentException("A target texture path requires a persistent asset identity.", nameof(textureId));
        return Path.Combine(
            "TargetArtifacts",
            "Textures",
            textureId.ToString("D", CultureInfo.InvariantCulture) + ".ktx");
    }

    private static string HashVariant(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
