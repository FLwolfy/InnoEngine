using System;

namespace Inno.References;

/// <summary>
/// Preserves the stable intent and diagnostic metadata for one logical object reference.
/// </summary>
public sealed class ReferenceDescriptor
{
    /// <summary>
    /// Creates a persistent reference descriptor.
    /// </summary>
    /// <param name="kindId">
    /// The open protocol used to resolve the target.
    /// </param>
    /// <param name="targetPersistentId">
    /// The target object's persistent identity, or an empty value for an explicitly unassigned slot.
    /// </param>
    /// <param name="expectedStableTypeId">
    /// An optional stable type constraint interpreted by the resolver.
    /// </param>
    /// <param name="lastKnownName">
    /// Optional non-authoritative display text retained while the target is missing.
    /// </param>
    /// <param name="lastKnownPath">
    /// Optional non-authoritative source path retained for diagnostics.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="kindId"/> is invalid.
    /// </exception>
    public ReferenceDescriptor(
        ReferenceKindId kindId,
        Guid targetPersistentId,
        Guid expectedStableTypeId = default,
        string? lastKnownName = null,
        string? lastKnownPath = null)
    {
        if (!kindId.isValid)
            throw new ArgumentException("A reference descriptor requires a valid kind identifier.", nameof(kindId));
        this.kindId = kindId;
        this.targetPersistentId = targetPersistentId;
        this.expectedStableTypeId = expectedStableTypeId;
        this.lastKnownName = lastKnownName;
        this.lastKnownPath = lastKnownPath;
    }

    /// <summary>
    /// Gets the protocol used to resolve this reference.
    /// </summary>
    public ReferenceKindId kindId { get; }

    /// <summary>
    /// Gets the persistent identity of the intended target, or an empty value when unassigned.
    /// </summary>
    public Guid targetPersistentId { get; }

    /// <summary>
    /// Gets the optional stable type constraint interpreted by the selected resolver.
    /// </summary>
    public Guid expectedStableTypeId { get; }

    /// <summary>
    /// Gets optional display text retained only for diagnostics and missing-state presentation.
    /// </summary>
    public string? lastKnownName { get; }

    /// <summary>
    /// Gets an optional last-known source path that is never used as resolution authority.
    /// </summary>
    public string? lastKnownPath { get; }

    /// <summary>
    /// Gets whether the user explicitly left this reference unassigned.
    /// </summary>
    public bool isUnassigned => targetPersistentId == Guid.Empty;
}
