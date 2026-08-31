using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Inno.Core.Identity;

/// <summary>
/// Concrete identity registry that manages persistent id and runtime id mappings.
/// </summary>
internal sealed class IdentityRegistry
{
    private readonly ReaderWriterLockSlim m_lock = new(LockRecursionPolicy.NoRecursion);

    private readonly List<RegistryEntry> m_active = [];

    private readonly ConditionalWeakTable<object, RegistrySlot> m_slotByObject = new();

    private readonly List<int> m_denseToSlot = new();
    private readonly List<int> m_sparseToDense = new();
    private readonly List<int> m_generations = new();
    private readonly Stack<int> m_freeSlots = new();

    private readonly Dictionary<Guid, int> m_slotByPersistent = new();

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

    /// <summary>
    /// Registers an object and binds a runtime id, with optional persistent id override.
    /// </summary>
    /// <param name="obj">Identity object to register.</param>
    /// <param name="persistentId">
    /// Preferred persistent id. When null, uses object's current identity persistent id.
    /// </param>
    /// <returns><see langword="true"/> when registered; <see langword="false"/> when already registered.</returns>
    public bool Register(IIdentityObject obj, Guid? persistentId = null)
    {
        ArgumentNullException.ThrowIfNull(obj);

        m_lock.EnterWriteLock();
        try
        {
            Identity identity = obj.GetIdentity();
            Guid resolvedPersistentId = persistentId.GetValueOrDefault(identity.persistentId);
            if (resolvedPersistentId == Guid.Empty)
            {
                resolvedPersistentId = Guid.NewGuid();
            }

            if (TryGetSlotByObjectNoLock(obj, out int objectSlot))
            {
                if (TryGetLiveObjectBySlotNoLock(objectSlot, out _, out _))
                {
                    return false;
                }

                RemoveSlotByObjectNoLock(obj, objectSlot);
            }

            if (TryGetSlotByPersistentNoLock(resolvedPersistentId, out int existingSlot))
            {
                if (TryGetLiveObjectBySlotNoLock(existingSlot, out IIdentityObject? existing, out _)
                    && !ReferenceEquals(existing, obj))
                {
                    throw new InvalidOperationException(
                        $"Persistent id '{resolvedPersistentId}' is already registered.");
                }

                RemoveSlotBySlotNoLock(existingSlot);
            }

            int slot = AllocateSlot();
            int generation = m_generations[slot];
            int runtimeId = RuntimeIdCodec.Pack(slot, generation);

            int denseIndex = m_active.Count;
            m_active.Add(new RegistryEntry(obj, resolvedPersistentId));
            m_slotByObject.Remove(obj);
            m_slotByObject.Add(obj, new RegistrySlot(slot));
            m_denseToSlot.Add(slot);
            m_sparseToDense[slot] = denseIndex;

            m_slotByPersistent[resolvedPersistentId] = slot;

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
            if (!TryGetSlotByObjectNoLock(obj, out int objectSlot))
            {
                return false;
            }

            if (!TryGetLiveEntryBySlotNoLock(objectSlot, out RegistryEntry? entry, out int denseIndex))
            {
                m_slotByObject.Remove(obj);
                return false;
            }

            Identity identity = obj.GetIdentity();
            if (!TryGetLiveObject(entry!, out IIdentityObject? activeObject))
            {
                m_slotByObject.Remove(obj);
                RemoveSlotBySlotNoLock(objectSlot);
                return false;
            }

            if (!ReferenceEquals(activeObject, obj))
                return false;

            RemoveSlotBySlotNoLock(objectSlot);
            identity.Unbind(this);
            obj.SetIdentity(identity);
            return true;
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    public TIdentity? Get<TIdentity>(int runtimeId)
        where TIdentity : class, IIdentityObject
    {
        int slot = -1;
        bool shouldCleanup = false;
        TIdentity? result = null;
        m_lock.EnterReadLock();
        try
        {
            if (!TryResolveDenseIndexNoLock(runtimeId, out int denseIndex))
            {
                return null;
            }

            if (!TryGetEntryByDenseNoLock(denseIndex, out RegistryEntry? entry))
            {
                return null;
            }

            if (!entry!.TryGetObject(out IIdentityObject? obj))
            {
                slot = m_denseToSlot[denseIndex];
                shouldCleanup = true;
            }
            else if (obj is TIdentity typed)
            {
                result = typed;
            }
        }
        finally
        {
            m_lock.ExitReadLock();
        }

        if (shouldCleanup && slot >= 0)
        {
            m_lock.EnterWriteLock();
            try
            {
                CleanupStaleSlotBySlotNoLock(slot);
            }
            finally
            {
                m_lock.ExitWriteLock();
            }
        }

        return result;
    }

    public TIdentity? Get<TIdentity>(Guid persistentId)
        where TIdentity : class, IIdentityObject
    {
        if (persistentId == Guid.Empty)
        {
            return null;
        }

        int slot = -1;
        bool shouldCleanup = false;
        TIdentity? result = null;
        m_lock.EnterReadLock();
        try
        {
            if (!m_slotByPersistent.TryGetValue(persistentId, out slot))
            {
                return null;
            }

            if (!TryGetDenseIndexBySlotNoLock(slot, out int denseIndex))
            {
                return null;
            }

            if (!TryGetEntryByDenseNoLock(denseIndex, out RegistryEntry? entry))
            {
                return null;
            }

            if (!entry!.TryGetObject(out IIdentityObject? obj))
            {
                slot = m_denseToSlot[denseIndex];
                shouldCleanup = true;
            }
            else if (obj is TIdentity typed)
            {
                result = typed;
            }
        }
        finally
        {
            m_lock.ExitReadLock();
        }

        if (shouldCleanup && slot >= 0)
        {
            m_lock.EnterWriteLock();
            try
            {
                CleanupStaleSlotBySlotNoLock(slot);
            }
            finally
            {
                m_lock.ExitWriteLock();
            }
        }
        return result;
    }

    internal bool TryGetRuntimeId(in Identity identity, out int runtimeId)
    {
        runtimeId = 0;
        if (identity.persistentId == Guid.Empty || identity.rawRuntimeId == 0)
            return false;

        m_lock.EnterReadLock();
        try
        {
            if (!TryGetDenseIndexByRuntimeIdNoLock(identity.rawRuntimeId, out int denseIndex))
            {
                return false;
            }

            if (!TryGetEntryByDenseNoLock(denseIndex, out RegistryEntry? entry))
            {
                return false;
            }

            if (entry!.persistentId != identity.persistentId)
            {
                return false;
            }

            runtimeId = identity.rawRuntimeId;
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

    private bool TryGetDenseIndexBySlotNoLock(int slot, out int denseIndex)
    {
        if ((uint)slot >= (uint)m_sparseToDense.Count)
        {
            denseIndex = -1;
            return false;
        }

        denseIndex = m_sparseToDense[slot];
        return denseIndex >= 0 && denseIndex < m_active.Count;
    }

    private bool TryGetDenseIndexByRuntimeIdNoLock(int runtimeId, out int denseIndex)
    {
        return TryResolveDenseIndexNoLock(runtimeId, out denseIndex) && TryGetEntryByDenseNoLock(denseIndex, out _);
    }

    private bool TryGetEntryByDenseNoLock(int denseIndex, out RegistryEntry? entry)
    {
        if (denseIndex < 0 || denseIndex >= m_active.Count)
        {
            entry = null;
            return false;
        }

        entry = m_active[denseIndex];
        return entry.TryGetObject(out _);
    }

    private bool TryGetLiveObjectByDenseNoLock(int denseIndex, out IIdentityObject? obj)
    {
        if (!TryGetEntryByDenseNoLock(denseIndex, out RegistryEntry? entry))
        {
            obj = null;
            return false;
        }

        return entry!.TryGetObject(out obj);
    }

    private bool TryGetLiveObjectBySlotNoLock(int slot, out IIdentityObject? obj, out RegistryEntry? entry)
    {
        if (!TryGetDenseIndexBySlotNoLock(slot, out int denseIndex))
        {
            obj = null;
            entry = null;
            return false;
        }

        if (!TryGetEntryByDenseNoLock(denseIndex, out entry))
        {
            obj = null;
            return false;
        }

        return entry!.TryGetObject(out obj);
    }

    private bool TryGetLiveEntryBySlotNoLock(int slot, out RegistryEntry? entry, out int denseIndex)
    {
        if (!TryGetDenseIndexBySlotNoLock(slot, out denseIndex))
        {
            entry = null;
            return false;
        }

        return TryGetEntryByDenseNoLock(denseIndex, out entry);
    }

    private bool TryGetSlotByObjectNoLock(IIdentityObject obj, out int slot)
    {
        if (m_slotByObject.TryGetValue(obj, out RegistrySlot? slotInfo))
        {
            slot = slotInfo.slot;
            return true;
        }

        slot = 0;
        return false;
    }

    private bool TryGetSlotByPersistentNoLock(Guid persistentId, out int slot)
    {
        if (!m_slotByPersistent.TryGetValue(persistentId, out slot))
        {
            return false;
        }

        if (TryGetLiveObjectBySlotNoLock(slot, out _, out _))
        {
            return true;
        }

        RemoveSlotBySlotNoLock(slot);
        return false;
    }

    private bool TryGetLiveObject(RegistryEntry entry, out IIdentityObject? obj)
    {
        return entry.TryGetObject(out obj);
    }

    private void CleanupStaleSlotBySlotNoLock(int slot)
    {
        if (!TryGetDenseIndexBySlotNoLock(slot, out int denseIndex))
        {
            m_slotByPersistent.Remove(GetPersistentIdBySlotNoLock(slot));
            m_sparseToDense[slot] = -1;
            return;
        }

        if (TryGetEntryByDenseNoLock(denseIndex, out RegistryEntry? entry) &&
            entry!.TryGetObject(out _))
        {
            return;
        }

        RemoveSlotBySlotNoLock(slot, denseIndex);
    }

    private Guid GetPersistentIdBySlotNoLock(int slot)
    {
        if (slot < 0 || slot >= m_sparseToDense.Count)
            return Guid.Empty;

        int denseIndex = m_sparseToDense[slot];
        if (denseIndex < 0 || denseIndex >= m_active.Count)
            return Guid.Empty;

        return m_active[denseIndex].persistentId;
    }

    private void RemoveSlotByObjectNoLock(IIdentityObject obj, int objectSlot)
    {
        m_slotByObject.Remove(obj);
        if (TryGetDenseIndexBySlotNoLock(objectSlot, out int denseIndex))
        {
            RemoveSlotBySlotNoLock(objectSlot, denseIndex);
        }
    }

    private void RemoveSlotBySlotNoLock(int slot)
    {
        if (!TryGetDenseIndexBySlotNoLock(slot, out int denseIndex))
        {
            return;
        }

        RemoveSlotBySlotNoLock(slot, denseIndex);
    }

    private void RemoveSlotBySlotNoLock(int slot, int denseIndex)
    {
        RegistryEntry removed = m_active[denseIndex];
        RegistryEntry lastEntry = m_active[^1];
        int lastIndex = m_active.Count - 1;
        int lastSlot = m_denseToSlot[lastIndex];

        if (removed.TryGetObject(out IIdentityObject? removedObj))
        {
            m_slotByObject.Remove(removedObj!);
        }

        if (m_slotByPersistent.TryGetValue(removed.persistentId, out int mappedSlot) &&
            mappedSlot == slot)
        {
            m_slotByPersistent.Remove(removed.persistentId);
        }

        m_active.RemoveAt(lastIndex);
        m_denseToSlot.RemoveAt(lastIndex);

        m_sparseToDense[slot] = -1;
        int nextGeneration = m_generations[slot] + 1;
        if (nextGeneration > RuntimeIdCodec.GENERATION_MASK)
        {
            nextGeneration = 1;
        }

        m_generations[slot] = nextGeneration;
        m_freeSlots.Push(slot);

        if (denseIndex == lastIndex)
        {
            return;
        }

        m_active[denseIndex] = lastEntry;
        m_denseToSlot[denseIndex] = lastSlot;
        m_sparseToDense[lastSlot] = denseIndex;
        if (m_active[denseIndex].TryGetObject(out IIdentityObject? movedObj))
        {
            if (m_slotByObject.TryGetValue(movedObj!, out RegistrySlot? movedSlot))
            {
                movedSlot.slot = lastSlot;
            }
        }
    }

    private sealed class RegistryEntry
    {
        public WeakReference<IIdentityObject> m_objectRef;
        public Guid persistentId;

        public RegistryEntry(IIdentityObject obj, Guid persistentId)
        {
            m_objectRef = new WeakReference<IIdentityObject>(obj);
            this.persistentId = persistentId;
        }

        public bool TryGetObject(out IIdentityObject? obj)
            => m_objectRef.TryGetTarget(out obj);
    }

    private sealed class RegistrySlot
    {
        public int slot;

        public RegistrySlot(int slot) => this.slot = slot;
    }
}
