using Inno.Editor.Assets.DragDrop;

using System;

namespace Inno.Editor.Assets.DragDrop;

/// <summary>Provides an assignable asset-reference property drop target.</summary>
public sealed class AssetReferenceDropTarget
{
    private readonly Action<Guid> m_assign;

    /// <summary>Creates an asset-reference drop target.</summary>
    public AssetReferenceDropTarget(Type expectedType, Action<Guid> assign)
    {
        this.expectedType = expectedType ?? throw new ArgumentNullException(nameof(expectedType));
        m_assign = assign ?? throw new ArgumentNullException(nameof(assign));
    }

    /// <summary>Gets the required asset type.</summary>
    public Type expectedType { get; }

    /// <summary>Assigns a persistent asset identity to the property.</summary>
    public void Assign(Guid persistentId) => m_assign(persistentId);
}
