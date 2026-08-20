using Inno.Editor.Assets.DragDrop;

using System;

namespace Inno.Editor.Assets.DragDrop;

/// <summary>Provides an assignable asset-reference property drop target.</summary>
public sealed class AssetReferenceDropTarget
{
    private readonly Action<Guid> m_assign;

    /// <summary>
    /// Creates a drop target that validates an asset type and assigns its persistent identity to a property.
    /// </summary>
    /// <param name="expectedType">The runtime asset type accepted by the property.</param>
    /// <param name="assign">The callback that writes an accepted persistent identity to the property.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null"/>.</exception>
    public AssetReferenceDropTarget(Type expectedType, Action<Guid> assign)
    {
        this.expectedType = expectedType ?? throw new ArgumentNullException(nameof(expectedType));
        m_assign = assign ?? throw new ArgumentNullException(nameof(assign));
    }

    /// <summary>Gets the required asset type.</summary>
    public Type expectedType { get; }

    /// <summary>
    /// Assigns a persistent asset identity to the represented property.
    /// </summary>
    /// <param name="persistentId">The stable identity of the accepted asset.</param>
    public void Assign(Guid persistentId) => m_assign(persistentId);
}
