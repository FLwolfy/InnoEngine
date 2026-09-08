using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Scene;

internal sealed class PrefabOverrideSet
{
    private readonly Dictionary<(Guid sourceComponentId, string propertyName), PrefabPropertyOverride>
        m_properties = [];
    private readonly Dictionary<Guid, PrefabStructureOverride> m_structures = [];
    private readonly HashSet<Guid> m_removedObjects = [];
    private readonly HashSet<Guid> m_removedComponents = [];
    private readonly HashSet<Guid> m_addedObjects = [];
    private readonly HashSet<Guid> m_addedComponents = [];

    internal IReadOnlyCollection<PrefabPropertyOverride> properties => m_properties.Values;
    internal IReadOnlyCollection<PrefabStructureOverride> structures => m_structures.Values;
    internal IReadOnlyCollection<Guid> removedObjects => m_removedObjects;
    internal IReadOnlyCollection<Guid> removedComponents => m_removedComponents;
    internal IReadOnlyCollection<Guid> addedObjects => m_addedObjects;
    internal IReadOnlyCollection<Guid> addedComponents => m_addedComponents;
    internal int count =>
        m_properties.Count + m_structures.Count +
        m_removedObjects.Count + m_removedComponents.Count +
        m_addedObjects.Count + m_addedComponents.Count;
    internal int orphanedCount =>
        m_properties.Values.Count(static item => item.isOrphaned) +
        m_structures.Values.Count(static item => item.isOrphaned);

    internal bool IsPropertyOverridden(Guid sourceComponentId, string propertyName)
        => m_properties.ContainsKey((sourceComponentId, propertyName));

    internal PrefabObjectOverrideKind GetStructureOverride(Guid sourceObjectId)
        => m_structures.TryGetValue(sourceObjectId, out PrefabStructureOverride? value)
            ? value.kind
            : PrefabObjectOverrideKind.None;

    internal void SetProperty(PrefabPropertyOverride value)
    {
        ArgumentNullException.ThrowIfNull(value);
        m_properties[(value.sourceComponentId, value.propertyName)] = value;
    }

    internal void SetStructure(Guid sourceObjectId, PrefabObjectOverrideKind kind, bool isOrphaned = false)
    {
        if (kind == PrefabObjectOverrideKind.None)
        {
            m_structures.Remove(sourceObjectId);
            return;
        }
        m_structures[sourceObjectId] = new PrefabStructureOverride(sourceObjectId, kind, isOrphaned);
    }

    internal void MarkObjectRemoved(Guid sourceObjectId) => m_removedObjects.Add(sourceObjectId);
    internal void MarkComponentRemoved(Guid sourceComponentId) => m_removedComponents.Add(sourceComponentId);
    internal void MarkObjectAdded(Guid instanceObjectId) => m_addedObjects.Add(instanceObjectId);
    internal void MarkComponentAdded(Guid instanceComponentId) => m_addedComponents.Add(instanceComponentId);

    internal bool IsObjectRemoved(Guid sourceObjectId) => m_removedObjects.Contains(sourceObjectId);
    internal bool IsComponentRemoved(Guid sourceComponentId) => m_removedComponents.Contains(sourceComponentId);
}
