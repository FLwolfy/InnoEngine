using System;
using System.Collections.Generic;

using Inno.Assets;

namespace Inno.Scene;

internal sealed class PrefabConnectionRecord
{
    private readonly Dictionary<Guid, Guid> m_objectIdentities = [];
    private readonly Dictionary<Guid, Guid> m_componentIdentities = [];

    internal PrefabConnectionRecord(
        AssetObject sourceAsset,
        Guid sourceRootId,
        bool isVariant,
        PrefabOverrideSet? overrides = null)
    {
        this.sourceAsset = sourceAsset ?? throw new ArgumentNullException(nameof(sourceAsset));
        this.sourceRootId = sourceRootId;
        this.isVariant = isVariant;
        this.overrides = overrides ?? new PrefabOverrideSet();
    }

    internal AssetObject sourceAsset { get; }
    internal Guid sourceRootId { get; }
    internal bool isVariant { get; }
    internal PrefabOverrideSet overrides { get; set; }
    internal IReadOnlyDictionary<Guid, Guid> objectIdentities => m_objectIdentities;
    internal IReadOnlyDictionary<Guid, Guid> componentIdentities => m_componentIdentities;

    internal void MapObject(Guid sourceId, GameObject gameObject)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        m_objectIdentities[sourceId] = gameObject.identity.persistentId;
    }

    internal void MapComponent(Guid sourceId, GameComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        m_componentIdentities[sourceId] = component.identity.persistentId;
    }

    internal void RemoveObject(Guid sourceId) => m_objectIdentities.Remove(sourceId);
    internal void RemoveComponent(Guid sourceId) => m_componentIdentities.Remove(sourceId);
}
