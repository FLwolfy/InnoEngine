using System;

using Inno.Extensibility.Types;
using Inno.Scripting.Api;

namespace Inno.Assets;

/// <summary>
/// Describes a persistent direct dependency on another asset.
/// </summary>
public readonly struct AssetDependency : IEquatable<AssetDependency>
{
    /// <summary>
    /// Creates an asset dependency descriptor.
    /// </summary>
    /// <param name="persistentId">
    /// The persistent identity of the referenced asset.
    /// </param>
    /// <param name="type">
    /// The reload-safe identity of the expected asset type.
    /// </param>
    /// <param name="lastKnownPath">
    /// The last known source-relative path.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="persistentId"/> is empty.
    /// </exception>
    [ScriptingApiIgnore]
    public AssetDependency(Guid persistentId, TypeRef type, string lastKnownPath)
    {
        if (persistentId == Guid.Empty)
            throw new ArgumentException("An asset dependency requires a persistent identity.", nameof(persistentId));
        this.persistentId = persistentId;
        this.type = type;
        this.lastKnownPath = lastKnownPath ?? string.Empty;
    }

    /// <summary>
    /// Gets the persistent identity of the referenced asset.
    /// </summary>
    public Guid persistentId { get; }

    /// <summary>
    /// Gets the reload-safe identity of the expected asset type.
    /// </summary>
    [ScriptingApiIgnore]
    public TypeRef type { get; }

    /// <summary>
    /// Gets the last known source-relative path.
    /// </summary>
    public string lastKnownPath { get; }

    /// <summary>
    /// Determines whether this instance and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="other">
    /// The value to compare with this instance.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both values represent the same logical state; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(AssetDependency other) => persistentId == other.persistentId;

    /// <summary>
    /// Determines whether this instance and the supplied value represent the same logical state.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when both values represent the same logical state; otherwise, <see langword="false"/>.
    /// </returns>
    /// <param name="obj">
    /// The object to compare with this instance.
    /// </param>
    public override bool Equals(object? obj) => obj is AssetDependency other && Equals(other);

    /// <summary>
    /// Computes a hash code from the fields that participate in logical equality.
    /// </summary>
    /// <returns>
    /// A hash code consistent with the implemented equality contract.
    /// </returns>
    public override int GetHashCode() => persistentId.GetHashCode();

    /// <summary>
    /// Determines whether two descriptors refer to the same persistent asset.
    /// </summary>
    /// <param name="left">
    /// The first dependency descriptor to compare.
    /// </param>
    /// <param name="right">
    /// The second dependency descriptor to compare.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both descriptors reference the same persistent asset; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator ==(AssetDependency left, AssetDependency right) => left.Equals(right);

    /// <summary>
    /// Determines whether two descriptors refer to different persistent assets.
    /// </summary>
    /// <param name="left">
    /// The first dependency descriptor to compare.
    /// </param>
    /// <param name="right">
    /// The second dependency descriptor to compare.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the descriptors reference different persistent assets; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator !=(AssetDependency left, AssetDependency right) => !left.Equals(right);
}
