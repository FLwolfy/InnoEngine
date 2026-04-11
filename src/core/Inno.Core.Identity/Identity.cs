using System;

namespace Inno.Core.Identity;

/// <summary>
/// Identity payload for an object, carrying persistent id and runtime id.
/// </summary>
public struct Identity
{
    public Guid persistentId { get; private set; }
    public int? runtimeId
    {
        get
        {
            if (m_registryRef == null || !m_registryRef.TryGetTarget(out IdentityRegistry? registry))
                return null;

            return registry.TryGetRuntimeId(this, out int runtimeId) ? runtimeId : null;
        }
    }

    private int m_runtimeId;
    private WeakReference<IdentityRegistry>? m_registryRef;

    public Identity(Guid persistentId)
    {
        this.persistentId = persistentId;
        m_runtimeId = 0;
        m_registryRef = null;
    }

    internal void Bind(IdentityRegistry registry, int runtimeId)
    {
        if (persistentId == Guid.Empty)
            persistentId = Guid.NewGuid();

        m_runtimeId = runtimeId;
        m_registryRef = new WeakReference<IdentityRegistry>(registry);
    }

    internal void Unbind(IdentityRegistry registry)
    {
        if (m_registryRef != null &&
            m_registryRef.TryGetTarget(out IdentityRegistry? current) &&
            ReferenceEquals(current, registry))
        {
            m_runtimeId = 0;
            m_registryRef = null;
        }
    }

    internal int rawRuntimeId => m_runtimeId;
}
