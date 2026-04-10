using System;

namespace Inno.Core.Identity;

/// <summary>
/// Identity payload for an object, carrying persistent id and runtime id.
/// </summary>
public struct Identity
{
    public Guid persistentId { get; private set; }
    public int runtimeId { get; private set; }

    private WeakReference<IdentityRegistry>? m_registryRef;

    public Identity(Guid persistentId)
    {
        this.persistentId = persistentId;
        runtimeId = 0;
        m_registryRef = null;
    }

    /// <summary>
    /// Returns a live runtime id only when this identity is still registered to a live registry.
    /// </summary>
    public bool TryGetRuntimeId(out int runtimeId)
    {
        runtimeId = 0;

        if (m_registryRef == null || !m_registryRef.TryGetTarget(out IdentityRegistry? registry))
            return false;

        return registry.TryGetRuntimeId(this, out runtimeId);
    }

    internal void Bind(IdentityRegistry registry, int runtimeId)
    {
        if (persistentId == Guid.Empty)
            persistentId = Guid.NewGuid();

        this.runtimeId = runtimeId;
        m_registryRef = new WeakReference<IdentityRegistry>(registry);
    }

    internal void Unbind(IdentityRegistry registry)
    {
        if (m_registryRef != null &&
            m_registryRef.TryGetTarget(out IdentityRegistry? current) &&
            ReferenceEquals(current, registry))
        {
            runtimeId = 0;
            m_registryRef = null;
        }
    }
}
