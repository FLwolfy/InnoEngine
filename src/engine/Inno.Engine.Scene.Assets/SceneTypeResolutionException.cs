using System;

namespace Inno.Engine.Scene.Assets;

/// <summary>
/// Reports that a serialized scene element cannot be created because its stable type is not present
/// in the active type catalog.
/// </summary>
public sealed class SceneTypeResolutionException : Exception
{
    /// <summary>
    /// Creates an exception for one unresolved serialized scene type.
    /// </summary>
    /// <param name="stableTypeId">The stable identity stored by the serialized scene element.</param>
    /// <param name="elementKind">The human-readable scene element kind, such as component or system.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="stableTypeId"/> is empty or <paramref name="elementKind"/> is empty.
    /// </exception>
    public SceneTypeResolutionException(Guid stableTypeId, string elementKind)
        : base($"Scene {elementKind} stable type id '{stableTypeId}' is not loaded.")
    {
        if (stableTypeId == Guid.Empty)
            throw new ArgumentException("A stable scene type identity is required.", nameof(stableTypeId));
        ArgumentException.ThrowIfNullOrWhiteSpace(elementKind);
        this.stableTypeId = stableTypeId;
        this.elementKind = elementKind;
    }

    /// <summary>
    /// Gets the unresolved stable type identity.
    /// </summary>
    public Guid stableTypeId { get; }

    /// <summary>
    /// Gets the scene element kind that expected the missing type.
    /// </summary>
    public string elementKind { get; }
}
