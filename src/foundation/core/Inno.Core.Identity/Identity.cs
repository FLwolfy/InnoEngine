using System;

namespace Inno.Core.Identity;

/// <summary>
/// Identity payload for an object, carrying persistent id and runtime id.
/// </summary>
public struct Identity
{
    /// <summary>
    /// Gets the stable identifier preserved by serialization and runtime reconstruction.
    /// </summary>
    public Guid persistentId { get; private set; }

    /// <summary>
    /// Gets the identifier assigned by the currently bound runtime registry.
    /// </summary>
    /// <remarks>
    /// The value is <see langword="null"/> while this identity is not bound to a live registry.
    /// </remarks>
    public int? runtimeId
    {
        get
        {
            if (m_registryRef == null || !m_registryRef.TryGetTarget(out IdentityRegistry? registry))
                return null;

            return registry.TryGetRuntimeId(this, out int runtimeId) ? runtimeId : null;
        }
    }

    /// <summary>
    /// Gets the domain-qualified runtime identity while this object is registered.
    /// </summary>
    /// <remarks>
    /// The value is <see langword="null"/> when this identity is detached or its registry is no longer reachable.
    /// </remarks>
    public RuntimeIdentity? runtimeIdentity
    {
        get
        {
            if (m_registryRef == null || !m_registryRef.TryGetTarget(out IdentityRegistry? registry))
                return null;
            return registry.TryGetRuntimeId(this, out int runtimeId)
                ? new RuntimeIdentity(registry.domainId, runtimeId)
                : null;
        }
    }

    private int m_runtimeId;
    private WeakReference<IdentityRegistry>? m_registryRef;

    /// <summary>
    /// Creates an unbound identity with the supplied persistent identifier.
    /// </summary>
    /// <param name="persistentId">
    /// The stable identifier to preserve across serialization boundaries.
    /// </param>
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

    /// <summary>
    /// Resolves this transient snapshot through its original weak identity registry.
    /// </summary>
    /// <typeparam name="TObject">
    /// The expected identity object type.
    /// </typeparam>
    /// <returns>
    /// The current object, or null if the registry retired, the slot changed generation, or the type differs.
    /// </returns>
    public TObject? Resolve<TObject>() where TObject : IdentityObject
        => m_registryRef is not null && m_registryRef.TryGetTarget(out IdentityRegistry? registry)
            ? registry.Get<TObject>(m_runtimeId)
            : null;
}
