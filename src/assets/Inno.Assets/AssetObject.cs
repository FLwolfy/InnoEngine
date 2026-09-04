using System;
using System.IO;

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

    /// <summary>
    /// Releases runtime resources when an unreachable asset is finalized.
    /// </summary>
    ~AssetObject()
    {
        ReleaseRuntimeResources();
    }

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
    /// Called once before runtime resources owned by this asset are released.
    /// </summary>
    protected virtual void OnUnloading()
    {
    }

    internal string sourceHash => m_sourceHash;

    internal void InitializeRuntimeState(
        AssetPath assetPath,
        string sourceHash,
        ReadOnlyMemory<byte> payload,
        bool isMissing,
        long version)
    {
        ReadOnlyMemory<byte> previous = m_runtimePayload;
        this.assetPath = assetPath;
        m_sourceHash = sourceHash ?? string.Empty;
        m_runtimePayload = payload.ToArray();
        m_isMissing = isMissing;
        m_contentVersion = version;
        if (m_runtimeResourcesReleased)
            GC.ReRegisterForFinalize(this);
        m_runtimeResourcesReleased = false;
        OnRuntimePayloadChanged(previous, m_runtimePayload);
    }

    internal void UpdateAssetPath(AssetPath assetPath)
    {
        this.assetPath = assetPath;
    }

    internal void ReleaseRuntimeResources()
    {
        if (m_runtimeResourcesReleased)
            return;
        m_runtimeResourcesReleased = true;
        OnUnloading();
        m_runtimePayload = [];
    }
}
