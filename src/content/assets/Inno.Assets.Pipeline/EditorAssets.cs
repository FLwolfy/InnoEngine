using System;

using Inno.Assets;
using Inno.Core.Serialization;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Provides Editor-script asset mutations through the authoring pipeline bound by the current host.
/// </summary>
public static class EditorAssets
{
    /// <summary>Captures reload-safe settings using the currently bound authoring owner's converters and references.</summary>
    /// <typeparam name="TValue">Current serializable settings type.</typeparam>
    /// <param name="value">Settings to capture without saving or mutating their referenced assets.</param>
    /// <returns>Native properties, stable type identity and automatically collected dependencies.</returns>
    public static AssetPropertySnapshot CaptureProperties<TValue>(TValue value) where TValue : class, ISerializable
    {
        if (AssetExecutionContext.current is not AssetPipeline pipeline)
            throw new InvalidOperationException("Capturing authoring settings requires an asset pipeline execution context.");
        return pipeline.CaptureProperties(value);
    }
    /// <summary>
    /// Creates or replaces a writable project asset source and imports the committed result.
    /// </summary>
    /// <param name="path">
    /// The writable project asset path.
    /// </param>
    /// <param name="asset">
    /// The complete source value to persist.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the source was committed and imported successfully.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="asset"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the current execution context is not bound to an authoring asset pipeline.
    /// </exception>
    public static bool Save(AssetPath path, AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (AssetExecutionContext.current is not AssetPipeline pipeline)
        {
            throw new InvalidOperationException(
                "Editor asset mutation requires an authoring asset pipeline execution context.");
        }
        return pipeline.Save(path, asset);
    }
}
