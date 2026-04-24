using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Inno.Core.Identity;

/// <summary>
/// Concrete identity registry that manages persistent id and runtime id mappings.
/// </summary>
public sealed class IdentityRegistry
{
    private readonly ReaderWriterLockSlim m_lock = new(LockRecursionPolicy.NoRecursion);

    private readonly List<IIdentityObject> m_active = new();
    private readonly Dictionary<IIdentityObject, int> m_denseIndexByObject = new(ReferenceComparer<IIdentityObject>.INSTANCE);
    private readonly List<int> m_denseToSlot = new();
    private readonly List<int> m_sparseToDense = new();
    private readonly List<int> m_generations = new();
    private readonly Stack<int> m_freeSlots = new();

    private readonly Dictionary<Guid, IIdentityObject> m_objectByPersistent = new();
    private readonly Dictionary<Guid, int> m_runtimeByPersistent = new();

    public int count
    {
        get
        {
            m_lock.EnterReadLock();
            try
            {
                return m_active.Count;
            }
            finally
            {
                m_lock.ExitReadLock();
            }
        }
    }

    public bool Register(IIdentityObject obj)
    {
        return Register(obj, persistentId: null);
    }

    /// <summary>
    /// Registers an object and binds a runtime id, with optional persistent id override.
    /// </summary>
    /// <param name="obj">Identity object to register.</param>
    /// <param name="persistentId">
    /// Preferred persistent id. When null, uses object's current identity persistent id.
    /// </param>
    /// <returns><see langword="true"/> when registered; <see langword="false"/> when already registered.</returns>
    public bool Register(IIdentityObject obj, Guid? persistentId)
    {
        ArgumentNullException.ThrowIfNull(obj);

        m_lock.EnterWriteLock();
        try
        {
            if (m_denseIndexByObject.ContainsKey(obj))
                return false;

            Identity identity = obj.GetIdentity();
            Guid resolvedPersistentId = persistentId.GetValueOrDefault(identity.persistentId);
            if (resolvedPersistentId == Guid.Empty)
                resolvedPersistentId = Guid.NewGuid();

            if (m_objectByPersistent.TryGetValue(resolvedPersistentId, out IIdentityObject? existing) && !ReferenceEquals(existing, obj))
                throw new InvalidOperationException($"Persistent id '{resolvedPersistentId}' is already registered.");

            int slot = AllocateSlot();
            int generation = m_generations[slot];
            int runtimeId = RuntimeIdCodec.Pack(slot, generation);

            int denseIndex = m_active.Count;
            m_active.Add(obj);
            m_denseIndexByObject[obj] = denseIndex;
            m_denseToSlot.Add(slot);
            m_sparseToDense[slot] = denseIndex;

            m_objectByPersistent[resolvedPersistentId] = obj;
            m_runtimeByPersistent[resolvedPersistentId] = runtimeId;

            identity = new Identity(resolvedPersistentId);
            identity.Bind(this, runtimeId);
            obj.SetIdentity(identity);
            return true;
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    public bool Unregister(IIdentityObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        m_lock.EnterWriteLock();
        try
        {
            if (!m_denseIndexByObject.TryGetValue(obj, out int denseIndex))
                return false;

            Identity identity = obj.GetIdentity();
            Guid persistentId = identity.persistentId;

            int lastIndex = m_active.Count - 1;
            IIdentityObject lastObj = m_active[lastIndex];
            int removedSlot = m_denseToSlot[denseIndex];
            int lastSlot = m_denseToSlot[lastIndex];

            m_active.RemoveAt(lastIndex);
            m_denseToSlot.RemoveAt(lastIndex);
            m_denseIndexByObject.Remove(obj);

            if (denseIndex != lastIndex)
            {
                m_active[denseIndex] = lastObj;
                m_denseIndexByObject[lastObj] = denseIndex;
                m_denseToSlot[denseIndex] = lastSlot;
                m_sparseToDense[lastSlot] = denseIndex;
            }

            m_sparseToDense[removedSlot] = -1;
            int nextGeneration = m_generations[removedSlot] + 1;
            if (nextGeneration > RuntimeIdCodec.GENERATION_MASK)
                nextGeneration = 1;
            m_generations[removedSlot] = nextGeneration;
            m_freeSlots.Push(removedSlot);

            if (persistentId != Guid.Empty)
            {
                m_objectByPersistent.Remove(persistentId);
                m_runtimeByPersistent.Remove(persistentId);
            }

            identity.Unbind(this);
            obj.SetIdentity(identity);
            return true;
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    public bool TryGet(int runtimeId, out IIdentityObject? obj)
    {
        m_lock.EnterReadLock();
        try
        {
            if (!TryResolveDenseIndexNoLock(runtimeId, out int denseIndex))
            {
                obj = null;
                return false;
            }

            obj = m_active[denseIndex];
            return true;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    public bool TryGet(Guid persistentId, out IIdentityObject? obj)
    {
        if (persistentId == Guid.Empty)
        {
            obj = null;
            return false;
        }

        m_lock.EnterReadLock();
        try
        {
            return m_objectByPersistent.TryGetValue(persistentId, out obj);
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    internal bool TryGetRuntimeId(in Identity identity, out int runtimeId)
    {
        runtimeId = 0;
        if (identity.persistentId == Guid.Empty || identity.rawRuntimeId == 0)
            return false;

        m_lock.EnterReadLock();
        try
        {
            if (!m_runtimeByPersistent.TryGetValue(identity.persistentId, out int currentRuntimeId))
                return false;

            if (currentRuntimeId != identity.rawRuntimeId)
                return false;

            if (!TryResolveDenseIndexNoLock(currentRuntimeId, out _))
                return false;

            runtimeId = currentRuntimeId;
            return true;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    private int AllocateSlot()
    {
        if (m_freeSlots.Count > 0)
            return m_freeSlots.Pop();

        int slot = m_generations.Count;
        m_generations.Add(1);
        m_sparseToDense.Add(-1);
        return slot;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryResolveDenseIndexNoLock(int runtimeId, out int denseIndex)
    {
        int slot = RuntimeIdCodec.UnpackSlot(runtimeId);
        int generation = RuntimeIdCodec.UnpackGeneration(runtimeId);
        if ((uint)slot >= (uint)m_generations.Count)
        {
            denseIndex = -1;
            return false;
        }

        if (m_generations[slot] != generation)
        {
            denseIndex = -1;
            return false;
        }

        denseIndex = m_sparseToDense[slot];
        return denseIndex >= 0 && denseIndex < m_active.Count;
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        internal static readonly ReferenceComparer<T> INSTANCE = new();

        public bool Equals(T? x, T? y)
            => ReferenceEquals(x, y);

        public int GetHashCode(T obj)
            => RuntimeHelpers.GetHashCode(obj);
    }

}
