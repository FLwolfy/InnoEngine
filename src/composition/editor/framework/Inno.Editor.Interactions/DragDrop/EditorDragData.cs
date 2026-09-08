using System;

using Inno.Core.Identity;

namespace Inno.Editor.Interactions;

/// <summary>
/// Contains the transient identity and preview label for one editor drag operation.
/// </summary>
public sealed class EditorDragData
{
    /// <summary>
    /// Creates drag data for a registered identity object.
    /// </summary>
    /// <param name="source">
    /// The registered source object resolved again for each drop query and delivery.
    /// </param>
    /// <param name="label">
    /// The human-readable label used by drag previews.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="source"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="source"/> is not registered in a live identity domain.
    /// </exception>
    public EditorDragData(IdentityObject source, string label)
    {
        ArgumentNullException.ThrowIfNull(source);
        sourceIdentity = source.identity.runtimeIdentity
            ?? throw new InvalidOperationException(
                "An editor drag source must be registered in a live identity domain.");
        this.label = label ?? string.Empty;
    }

    /// <summary>
    /// Gets the domain-qualified transient identity written to the native drag protocol.
    /// </summary>
    public RuntimeIdentity sourceIdentity { get; }

    /// <summary>
    /// Gets the drag preview label.
    /// </summary>
    public string label { get; }

}
