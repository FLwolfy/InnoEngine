using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Pipeline;
using Inno.Core.Serialization;

namespace Inno.Animation.Assets;

/// <summary>
/// Imports and exports structured <c>.ianim</c> animation clip sources.
/// </summary>
[AssetImporterExtension]
public sealed class AnimationClipImporter : AssetImporter<AnimationClipAsset>
{
    private static readonly IReadOnlyList<string> S_EXTENSIONS = [".ianim"];

    /// <summary>
    /// Gets the stable importer identifier used in artifact fingerprints.
    /// </summary>
    public override string importerId => "inno.animation.clip";

    /// <summary>
    /// Gets the current structured animation source extension.
    /// </summary>
    public override IReadOnlyList<string> supportedExtensions => S_EXTENSIONS;

    /// <summary>
    /// Imports one validated structured animation clip.
    /// </summary>
    /// <param name="context">
    /// Candidate source bytes and serialization generation.
    /// </param>
    /// <param name="output">
    /// Writer that receives the clip and immutable runtime artifact.
    /// </param>
    /// <param name="cancellationToken">
    /// Token that cancels artifact staging.
    /// </param>
    /// <returns>
    /// An operation that completes after the runtime artifact is staged.
    /// </returns>
    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<AnimationClipAsset> output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        AnimationClipSource source = context.serialization.Deserialize<AnimationClipSource>(
            context.sourceBytes.ToArray());
        var clip = new AnimationClipAsset
        {
            duration = source.duration,
            tracks = source.tracks,
            events = source.events
        };
        clip.Validate();
        output.SetAsset(clip);
        await output.WriteArtifactAsync(
            "runtime",
            context.sourceBytes,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Serializes one validated animation clip back to its writable source mount.
    /// </summary>
    /// <param name="context">
    /// Generation-bound source serialization services.
    /// </param>
    /// <param name="asset">
    /// Animation clip to export.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token observed before source bytes are returned.
    /// </param>
    /// <returns>
    /// The complete current structured source bytes.
    /// </returns>
    protected override ValueTask<ReadOnlyMemory<byte>?> ExportAsync(
        AssetExportContext context,
        AnimationClipAsset asset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        asset.Validate();
        byte[] bytes = context.serialization.Serialize(new AnimationClipSource
        {
            duration = asset.duration,
            tracks = asset.tracks,
            events = asset.events
        });
        return ValueTask.FromResult<ReadOnlyMemory<byte>?>(bytes);
    }

    private sealed class AnimationClipSource : ISerializable
    {
        [SerializableProperty]
        internal float duration { get; set; }

        [SerializableProperty]
        internal AnimationTrack[] tracks { get; set; } = [];

        [SerializableProperty]
        internal AnimationEventMarker[] events { get; set; } = [];
    }
}
