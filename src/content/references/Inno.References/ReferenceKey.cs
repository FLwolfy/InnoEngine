using System;

namespace Inno.References;

/// <summary>
/// Identifies one persistent reference slot owned by a logical object.
/// </summary>
public readonly struct ReferenceKey : IEquatable<ReferenceKey>
{
    /// <summary>
    /// Creates a key for one reference slot.
    /// </summary>
    /// <param name="ownerPersistentId">
    /// The persistent identity of the object that owns the slot.
    /// </param>
    /// <param name="path">
    /// The stable property or structural path inside the owner.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when the owner identity or path is empty.
    /// </exception>
    public ReferenceKey(Guid ownerPersistentId, string path)
    {
        if (ownerPersistentId == Guid.Empty)
            throw new ArgumentException("A reference key requires a non-empty owner identity.", nameof(ownerPersistentId));
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A reference key requires a non-empty stable path.", nameof(path));
        this.ownerPersistentId = ownerPersistentId;
        this.path = path;
    }

    /// <summary>
    /// Gets the persistent identity of the object that owns this reference slot.
    /// </summary>
    public Guid ownerPersistentId { get; }

    /// <summary>
    /// Gets the stable property or structural path inside the owner.
    /// </summary>
    public string path { get; }

    /// <summary>
    /// Determines whether two values identify the same owner slot.
    /// </summary>
    /// <param name="other">
    /// The key to compare with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the owner identity and path are equal; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(ReferenceKey other)
        => ownerPersistentId == other.ownerPersistentId && string.Equals(path, other.path, StringComparison.Ordinal);

    /// <summary>
    /// Determines whether an object identifies the same owner slot.
    /// </summary>
    /// <param name="obj">
    /// The object to compare with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the object is an equal key; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj) => obj is ReferenceKey other && Equals(other);

    /// <summary>
    /// Computes a hash code from the owner identity and stable path.
    /// </summary>
    /// <returns>
    /// A hash code consistent with reference-key equality.
    /// </returns>
    public override int GetHashCode() => HashCode.Combine(ownerPersistentId, StringComparer.Ordinal.GetHashCode(path ?? string.Empty));

    /// <summary>
    /// Formats this slot identity for diagnostics.
    /// </summary>
    /// <returns>
    /// The owner identity followed by the stable slot path.
    /// </returns>
    public override string ToString() => $"{ownerPersistentId:D}:{path}";

    /// <summary>
    /// Determines whether two reference keys are equal.
    /// </summary>
    /// <param name="left">
    /// The first key.
    /// </param>
    /// <param name="right">
    /// The second key.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the keys are equal; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator ==(ReferenceKey left, ReferenceKey right) => left.Equals(right);

    /// <summary>
    /// Determines whether two reference keys are different.
    /// </summary>
    /// <param name="left">
    /// The first key.
    /// </param>
    /// <param name="right">
    /// The second key.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the keys are different; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator !=(ReferenceKey left, ReferenceKey right) => !left.Equals(right);
}
