using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Inno.Assets;

/// <summary>
/// Provides script-facing asset queries through the lookup bound to the current runtime session.
/// </summary>
/// <remarks>
/// This façade owns no asset state. Engine and Editor infrastructure should depend on an explicit
/// <see cref="IAssetLookup"/> instance.
/// </remarks>
public static class Assets
{
    private const string C_ASSET_SOURCE_KEY = "Inno.AssetSource";

    /// <summary>
    /// Creates a path relative to the Asset source that owns the calling script assembly.
    /// </summary>
    /// <param name="localPath">
    /// The normalized path relative to the caller's Project or installed Plugin Assets root.
    /// </param>
    /// <returns>
    /// A mount-qualified path that follows the script from Project development into an installed Plugin.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the calling assembly was not produced by the Inno scripting compiler or has invalid
    /// Asset source ownership metadata.
    /// </exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static AssetPath LocalPath(string localPath)
    {
        Assembly caller = Assembly.GetCallingAssembly();
        AssemblyMetadataAttribute? metadata = caller
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(static value => string.Equals(
                value.Key,
                C_ASSET_SOURCE_KEY,
                StringComparison.Ordinal));
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Value))
        {
            throw new InvalidOperationException(
                $"Assembly '{caller.GetName().Name}' has no {C_ASSET_SOURCE_KEY} ownership metadata.");
        }

        try
        {
            return new AssetPath(new AssetSourceId(metadata.Value), localPath);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"Assembly '{caller.GetName().Name}' has invalid {C_ASSET_SOURCE_KEY} ownership metadata.",
                exception);
        }
    }

    /// <summary>
    /// Loads the canonical asset at a logical catalog path in the current session.
    /// </summary>
    /// <typeparam name="TAsset">
    /// The required asset contract.
    /// </typeparam>
    /// <param name="path">
    /// The mount-qualified logical catalog path.
    /// </param>
    /// <returns>
    /// The canonical compatible asset owned by the current lookup.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no asset lookup is active, the asset does not exist, or its type is incompatible.
    /// </exception>
    public static TAsset Load<TAsset>(AssetPath path)
        where TAsset : AssetObject
        => AssetExecutionContext.current.Load<TAsset>(path);

    /// <summary>
    /// Loads the canonical asset with a persistent identity in the current session.
    /// </summary>
    /// <typeparam name="TAsset">
    /// The required asset contract.
    /// </typeparam>
    /// <param name="persistentId">
    /// The non-empty persistent asset identity.
    /// </param>
    /// <returns>
    /// The canonical compatible asset owned by the current lookup.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no asset lookup is active, the asset does not exist, or its type is incompatible.
    /// </exception>
    public static TAsset Load<TAsset>(Guid persistentId)
        where TAsset : AssetObject
        => AssetExecutionContext.current.Load<TAsset>(persistentId);

    /// <summary>
    /// Tries to load the canonical asset at a logical catalog path in the current session.
    /// </summary>
    /// <typeparam name="TAsset">
    /// The required asset contract.
    /// </typeparam>
    /// <param name="path">
    /// The mount-qualified logical catalog path.
    /// </param>
    /// <param name="asset">
    /// Receives the canonical compatible asset when available.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the current lookup contains a compatible asset; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no asset lookup is active for the caller.
    /// </exception>
    public static bool TryLoad<TAsset>(AssetPath path, out TAsset? asset)
        where TAsset : AssetObject
        => AssetExecutionContext.current.TryLoad(path, out asset);

    /// <summary>
    /// Tries to load the canonical asset with a persistent identity in the current session.
    /// </summary>
    /// <typeparam name="TAsset">
    /// The required asset contract.
    /// </typeparam>
    /// <param name="persistentId">
    /// The non-empty persistent asset identity.
    /// </param>
    /// <param name="asset">
    /// Receives the canonical compatible asset when available.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the current lookup contains a compatible asset; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no asset lookup is active for the caller.
    /// </exception>
    public static bool TryLoad<TAsset>(Guid persistentId, out TAsset? asset)
        where TAsset : AssetObject
        => AssetExecutionContext.current.TryLoad(persistentId, out asset);

    /// <summary>
    /// Acquires a canonical asset and explicitly retains it until the returned lease is disposed.
    /// </summary>
    /// <typeparam name="TAsset">
    /// Required asset contract.
    /// </typeparam>
    /// <param name="path">
    /// Mount-qualified logical asset path.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation for this caller's asynchronous wait.
    /// </param>
    /// <returns>
    /// A lease that owns residency for the canonical asset.
    /// </returns>
    public static ValueTask<AssetLease<TAsset>> AcquireAsync<TAsset>(
        AssetPath path,
        CancellationToken cancellationToken = default)
        where TAsset : AssetObject
        => GetResidency().AcquireAsync<TAsset>(path, cancellationToken);

    /// <summary>
    /// Acquires a canonical asset by persistent identity and explicitly retains it.
    /// </summary>
    /// <typeparam name="TAsset">
    /// Required asset contract.
    /// </typeparam>
    /// <param name="persistentId">
    /// Persistent asset identity.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation for this caller's asynchronous wait.
    /// </param>
    /// <returns>
    /// A lease that owns residency for the canonical asset.
    /// </returns>
    public static ValueTask<AssetLease<TAsset>> AcquireAsync<TAsset>(
        Guid persistentId,
        CancellationToken cancellationToken = default)
        where TAsset : AssetObject
        => GetResidency().AcquireAsync<TAsset>(persistentId, cancellationToken);

    /// <summary>
    /// Acquires one verified immutable artifact output for an explicit lifetime.
    /// </summary>
    /// <param name="persistentId">
    /// Persistent identity of the artifact owner.
    /// </param>
    /// <param name="outputName">
    /// Stable artifact output name.
    /// </param>
    /// <returns>
    /// A lease over verified artifact metadata and bytes.
    /// </returns>
    public static ArtifactLease AcquireArtifact(Guid persistentId, string outputName)
        => GetResidency().AcquireArtifact(persistentId, outputName);

    private static IAssetResidency GetResidency()
        => AssetExecutionContext.current as IAssetResidency
            ?? throw new InvalidOperationException(
                "The current asset lookup does not provide explicit residency ownership.");
}
