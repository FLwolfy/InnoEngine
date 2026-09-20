using System;

using Inno.Assets;
using Inno.Core.Serialization;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Provides Editor-script asset mutations through the authoring pipeline bound by the current host.
/// </summary>
public static class EditorAssets
{
    /// <summary>Encodes a detached native asset with the current authoring pipeline's converters and reference context.</summary>
    /// <param name="asset">Complete detached value to encode without saving or importing it.</param>
    /// <returns>Reload-safe native asset bytes suitable for draft state and History payloads.</returns>
    public static byte[] EncodeNative(AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (AssetExecutionContext.current is not AssetPipeline pipeline)
            throw new InvalidOperationException(
                "Encoding an authoring asset requires an asset pipeline execution context.");
        return pipeline.CreateSourceStore().Encode(asset);
    }

    /// <summary>Decodes detached native asset bytes with the current authoring pipeline's converters and reference context.</summary>
    /// <typeparam name="TAsset">Expected native asset type.</typeparam>
    /// <param name="bytes">Bytes previously produced by the native asset source codec.</param>
    /// <returns>A detached asset value that can be edited without publishing it.</returns>
    public static TAsset DecodeNative<TAsset>(byte[] bytes) where TAsset : AssetObject
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (AssetExecutionContext.current is not AssetPipeline pipeline)
            throw new InvalidOperationException(
                "Decoding an authoring asset requires an asset pipeline execution context.");
        return pipeline.CreateSourceStore().Decode<TAsset>(bytes);
    }

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
    /// The complete source value to persist. Saving over an existing source retains that source's persistent identity.
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
