using System;
using System.IO;
using System.Reflection;

using Inno.Core.Identity;
using Inno.Core.Serialization;

namespace Inno.Assets.Core;

/// <summary>
/// Base runtime asset object that participates in identity and serialization.
/// </summary>
public abstract class AssetObject : ISerializable, IIdentityObject
{
    /// <summary>
    /// Relative path to the source file under <c>AssetManager.assetRoot</c>.
    /// </summary>
    [SerializableProperty(PropertyVisibility.Hide)]
    public string sourcePath { get; private set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash for source bytes used by import cache validation.
    /// </summary>
    [SerializableProperty(PropertyVisibility.Hide)]
    public string sourceHash { get; private set; } = string.Empty;

    /// <summary>
    /// Asset display name derived from <see cref="sourcePath"/>.
    /// </summary>
    public string name => string.IsNullOrWhiteSpace(sourcePath) ? GetType().Name : Path.GetFileName(sourcePath);

    /// <summary>
    /// Persistent identity id used for stable cross-session references.
    /// </summary>
    public Guid persistentId => ((IIdentityObject)this).GetIdentity().persistentId;

    /// <summary>
    /// Runtime identity id assigned while loaded in the current process.
    /// </summary>
    public int? runtimeId => ((IIdentityObject)this).GetIdentity().runtimeId;

    /// <summary>
    /// Runtime-ready artifact payload produced by importer.
    /// </summary>
    public ReadOnlyMemory<byte> runtimePayload => m_runtimePayload;

    private byte[] m_runtimePayload = [];

    internal void SetSourceInfo(string relativePath, string hash)
    {
        sourcePath = relativePath ?? string.Empty;
        sourceHash = hash ?? string.Empty;
    }

    internal void SetRuntimePayload(byte[] payload)
    {
        m_runtimePayload = payload ?? [];
    }

    internal void SetPersistentId(Guid persistentId)
    {
        if (persistentId == Guid.Empty)
            persistentId = Guid.NewGuid();

        var method = typeof(IIdentityObject).GetMethod(
            "SetIdentity",
            BindingFlags.Instance | BindingFlags.NonPublic);

        if (method == null)
            throw new InvalidOperationException("Failed to resolve IIdentityObject.SetIdentity.");

        method.Invoke(this, [new Identity(persistentId)]);
    }
}
