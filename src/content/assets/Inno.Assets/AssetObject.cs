using System;
using System.IO;

using Inno.Core.Execution;
using Inno.Core.Identity;
using Inno.Core.Serialization;

namespace Inno.Assets;

/// <summary>
/// Provides the common runtime identity and payload contract for imported assets.
/// </summary>
public abstract class AssetObject : IdentityObject, ISerializable
{
    private byte[] m_runtimePayload = [];
    private bool m_isMissing;
    private bool m_runtimeResourcesReleased;
    private long m_contentVersion;
    private string m_sourceHash = string.Empty;
    private object? m_runtimeOwner;
    private WeakReference<IAssetPropertyStateResolver>? m_propertyStateOwner;

    /// <summary>
    /// Gets the isolated source path associated with this asset.
    /// </summary>
    [SerializableProperty(PropertyVisibility.Hide)]
    public AssetPath assetPath { get; private set; } = AssetPath.Project(string.Empty);

    /// <summary>
    /// Gets a display name derived from the source-local path.
    /// </summary>
    public string name => string.IsNullOrWhiteSpace(assetPath.localPath)
        ? GetType().Name
        : Path.GetFileName(assetPath.localPath);

    /// <summary>
    /// Gets whether this instance represents an unavailable persistent asset.
    /// </summary>
    public bool isMissing => m_isMissing;

    /// <summary>
    /// Gets the version of the currently committed runtime content.
    /// </summary>
    public long contentVersion => m_contentVersion;

    /// <summary>
    /// Gets the runtime artifact payload produced by the importer.
    /// </summary>
    public ReadOnlyMemory<byte> runtimePayload => m_runtimePayload;

    /// <summary>Restores detached extension settings using this asset's actual owner and converter generation.</summary>
    /// <typeparam name="TValue">Current settings contract with a stable type identity.</typeparam>
    /// <param name="stableTypeId">Persisted settings type identity.</param>
    /// <param name="propertyData">Native property payload to restore.</param>
    /// <param name="target">Detached target, never retained by the asset.</param>
    /// <exception cref="InvalidOperationException">The asset is unowned, retired, or lacks property-state services.</exception>
    public void RestoreProperties<TValue>(Guid stableTypeId, byte[] propertyData, TValue target) where TValue : class, ISerializable
    {
        if (m_runtimeResourcesReleased || m_propertyStateOwner is null || !m_propertyStateOwner.TryGetTarget(out var owner))
            throw new InvalidOperationException("The asset's property-state owner is unavailable.");
        owner.RestoreProperties(stableTypeId, propertyData, target);
    }

    /// <summary>
    /// Called after a new runtime payload has been committed to this instance.
    /// </summary>
    /// <param name="previousPayload">
    /// The previously committed payload.
    /// </param>
    /// <param name="currentPayload">
    /// The newly committed payload.
    /// </param>
    protected virtual void OnRuntimePayloadChanged(
        ReadOnlyMemory<byte> previousPayload,
        ReadOnlyMemory<byte> currentPayload)
    {
    }

    /// <summary>
    /// Releases the runtime resources owned by this asset without clearing its payload before quiescence.
    /// </summary>
    /// <remarks>
    /// An unfinished release may throw RetirementPendingException. The owner retains the asset and retries
    /// this hook; completed steps must not be repeated. Other failures are terminal and are reported by the owner.
    /// </remarks>
    /// <exception cref="RetirementPendingException">
    /// Owned work has not quiesced; runtime payload and ownership must remain intact until a later attempt.
    /// </exception>
    protected virtual void OnUnloading()
    {
    }

    internal string sourceHash => m_sourceHash;

    internal void ClaimRuntimeOwner(object authority, WeakReference<IAssetPropertyStateResolver>? properties)
    {
        object? previous = System.Threading.Interlocked.CompareExchange(ref m_runtimeOwner, authority, null);
        if (previous is not null && !ReferenceEquals(previous, authority))
            throw new InvalidOperationException("The asset belongs to another runtime owner.");
        m_propertyStateOwner = properties;
    }

    internal void InitializeRuntimeState(
        AssetPath assetPath,
        string sourceHash,
        ReadOnlyMemory<byte> payload,
        bool isMissing,
        long version)
    {
        byte[] previous = m_runtimePayload;
        AssetPath previousPath = this.assetPath;
        string previousHash = m_sourceHash;
        bool previousMissing = m_isMissing;
        long previousVersion = m_contentVersion;
        bool previousReleased = m_runtimeResourcesReleased;
        this.assetPath = assetPath;
        m_sourceHash = sourceHash ?? string.Empty;
        m_runtimePayload = payload.ToArray();
        m_isMissing = isMissing;
        m_contentVersion = version;
        m_runtimeResourcesReleased = false;
        try
        {
            OnRuntimePayloadChanged(previous, m_runtimePayload);
        }
        catch
        {
            m_runtimePayload = previous;
            this.assetPath = previousPath;
            m_sourceHash = previousHash;
            m_isMissing = previousMissing;
            m_contentVersion = previousVersion;
            m_runtimeResourcesReleased = previousReleased;
            throw;
        }
    }

    internal void UpdateAssetPath(AssetPath assetPath)
    {
        this.assetPath = assetPath;
    }

    internal void ReleaseRuntimeResources()
    {
        if (m_runtimeResourcesReleased)
            return;
        try
        {
            OnUnloading();
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch
        {
            m_runtimeResourcesReleased = true;
            m_runtimePayload = [];
            throw;
        }
        m_runtimeResourcesReleased = true;
        m_runtimePayload = [];
    }
}
