using System;
using Inno.Assets;
using Inno.Core.Serialization;

namespace Inno.Rendering;

/// <summary>Restores neutral render settings through the canonical asset's actual owner and pinned references.</summary>
public sealed class RenderExtensionStateContext
{
    private readonly AssetObject m_owner;

    /// <summary>Binds restoration to a canonical asset, independently of the caller's ambient session.</summary>
    /// <param name="owner">Asset whose database and pinned converter generation own the settings.</param>
    /// <remarks>It must not outlive the asset generation. Authoring captures use the asset system's property snapshots.</remarks>
    public RenderExtensionStateContext(AssetObject owner)
        => m_owner = owner ?? throw new ArgumentNullException(nameof(owner));

    /// <summary>Restores a matching settings contract with this owner's asset references.</summary>
    /// <typeparam name="TSettings">Current settings type.</typeparam>
    /// <param name="state">Detached neutral source state.</param>
    /// <param name="target">Current generation target, never retained by this context.</param>
    public void Restore<TSettings>(SerializedRenderExtensionState state, TSettings target) where TSettings : class, ISerializable
    {
        ArgumentNullException.ThrowIfNull(target);
        m_owner.RestoreProperties(state.stableTypeId, state.propertyData, target);
    }
}
