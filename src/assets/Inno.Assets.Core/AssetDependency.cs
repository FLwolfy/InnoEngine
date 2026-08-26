using System;

using Inno.Core.Reflection;
using Inno.Core.Scripting;

namespace Inno.Assets.Core;

/// <summary>
/// Describes a persistent direct dependency on another asset.
/// </summary>
public readonly struct AssetDependency : IEquatable<AssetDependency>
{
    /// <summary>
    /// Creates an asset dependency descriptor.
    /// </summary>
    /// <param name="persistentId">The persistent identity of the referenced asset.</param>
    /// <param name="type">The reload-safe identity of the expected asset type.</param>
    /// <param name="lastKnownPath">The last known source-relative path.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="persistentId"/> is empty.</exception>
    [ScriptingApiIgnore]
    public AssetDependency(Guid persistentId, TypeRef type, string lastKnownPath)
    {
        if (persistentId == Guid.Empty)
            throw new ArgumentException("An asset dependency requires a persistent identity.", nameof(persistentId));
        this.persistentId = persistentId;
        this.type = type;
        this.lastKnownPath = lastKnownPath ?? string.Empty;
    }

    /// <summary>Gets the persistent identity of the referenced asset.</summary>
    public Guid persistentId { get; }

    /// <summary>Gets the reload-safe identity of the expected asset type.</summary>
    [ScriptingApiIgnore]
    public TypeRef type { get; }

    /// <summary>Gets the last known source-relative path.</summary>
    public string lastKnownPath { get; }

    /// <inheritdoc/>
    public bool Equals(AssetDependency other) => persistentId == other.persistentId;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is AssetDependency other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => persistentId.GetHashCode();

    /// <summary>Determines whether two descriptors refer to the same persistent asset.</summary>
    public static bool operator ==(AssetDependency left, AssetDependency right) => left.Equals(right);

    /// <summary>Determines whether two descriptors refer to different persistent assets.</summary>
    public static bool operator !=(AssetDependency left, AssetDependency right) => !left.Equals(right);
}
