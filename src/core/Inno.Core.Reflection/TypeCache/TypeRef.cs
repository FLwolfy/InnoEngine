using System;

namespace Inno.Core.Reflection;

/// <summary>
/// Identifies one logical runtime type without retaining its assembly or load context.
/// </summary>
/// <remarks>
/// Equality and persistence use <see cref="stableId"/>. The <see cref="runtimeId"/> is only a
/// generation-local resolution hint and can become stale after an assembly reload.
/// </remarks>
public readonly struct TypeRef : IEquatable<TypeRef>
{
    /// <summary>
    /// Creates an unresolved type reference from its persistent identity.
    /// </summary>
    /// <param name="stableId">The persistent type identity, or an empty value for an invalid reference.</param>
    public TypeRef(Guid stableId)
        : this(stableId, runtimeId: 0)
    {
    }

    internal TypeRef(Guid stableId, int runtimeId)
    {
        this.stableId = stableId;
        this.runtimeId = runtimeId;
    }

    /// <summary>
    /// Gets the persistent identity used across assembly generations and process launches.
    /// </summary>
    public Guid stableId { get; }

    /// <summary>
    /// Gets the generation-local lookup hint captured when this value was created.
    /// </summary>
    /// <remarks>This value must not be persisted or used as logical type identity.</remarks>
    public int runtimeId { get; }

    /// <summary>
    /// Gets whether the active type-cache generation currently resolves this reference.
    /// </summary>
    public bool isValid => stableId != Guid.Empty && TypeCacheManager.TryResolveCurrent(this, out _);

    /// <summary>
    /// Resolves this reference against the active type-cache generation.
    /// </summary>
    /// <returns>The active CLR type represented by this reference.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the reference is empty, the type cache is unavailable, or the logical type is not loaded.
    /// </exception>
    public Type Resolve() => TypeCacheManager.ResolveCurrent(this);

    /// <summary>
    /// Resolves this reference against a specific immutable type-cache snapshot.
    /// </summary>
    /// <param name="snapshot">The snapshot that defines the required assembly generation.</param>
    /// <returns>The CLR type represented by this reference in <paramref name="snapshot"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="snapshot"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the reference is empty or the logical type does not exist in the snapshot.
    /// </exception>
    public Type Resolve(TypeCacheSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (stableId != Guid.Empty && snapshot.TryResolve(this, out Type? type))
            return type!;
        throw CreateResolutionException(snapshot.version);
    }

    /// <inheritdoc />
    public bool Equals(TypeRef other) => stableId == other.stableId;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TypeRef other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => stableId.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => stableId == Guid.Empty ? "<invalid>" : stableId.ToString("D");

    /// <summary>Determines whether two references identify the same logical type.</summary>
    /// <param name="left">The first reference.</param>
    /// <param name="right">The second reference.</param>
    /// <returns><see langword="true"/> when both references have the same stable identity.</returns>
    public static bool operator ==(TypeRef left, TypeRef right) => left.Equals(right);

    /// <summary>Determines whether two references identify different logical types.</summary>
    /// <param name="left">The first reference.</param>
    /// <param name="right">The second reference.</param>
    /// <returns><see langword="true"/> when the references have different stable identities.</returns>
    public static bool operator !=(TypeRef left, TypeRef right) => !left.Equals(right);

    private InvalidOperationException CreateResolutionException(long? snapshotVersion = null)
    {
        string generation = snapshotVersion is long version
            ? $" in type-cache snapshot {version}"
            : string.Empty;
        return new InvalidOperationException(
            stableId == Guid.Empty
                ? "An empty type reference cannot be resolved."
                : $"Type reference '{stableId:D}' is not available{generation}.");
    }
}
