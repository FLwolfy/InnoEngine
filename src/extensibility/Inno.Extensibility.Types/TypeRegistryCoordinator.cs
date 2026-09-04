using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Inno.Extensibility.Types;

internal interface ITypeRegistry
{
    /// <summary>
    /// Builds and validates candidate state without changing the active generation.
    /// </summary>
    /// <param name="types">
    /// The active type catalog generation used for extension resolution.
    /// </param>
    /// <returns>
    /// The validated itype registry transaction that represents the completed operation.
    /// </returns>
    ITypeRegistryTransaction Prepare(TypeCacheSnapshot types);
}

internal interface ITypeRegistryTransaction
{
    /// <summary>
    /// Makes the prepared value active at the owning subsystem's safety point.
    /// </summary>
    void Activate();
    /// <summary>
    /// Finalizes candidate activation and releases temporary transaction state.
    /// </summary>
    void Complete();
    /// <summary>
    /// Restores the state captured before the current transaction began.
    /// </summary>
    void Rollback();
}

internal sealed class TypeRegistryNoopTransaction : ITypeRegistryTransaction
{
    internal static TypeRegistryNoopTransaction instance { get; } = new();

    /// <summary>
    /// Makes the prepared value active at the owning subsystem's safety point.
    /// </summary>
    public void Activate()
    {
    }

    /// <summary>
    /// Finalizes candidate activation and releases temporary transaction state.
    /// </summary>
    public void Complete()
    {
    }

    /// <summary>
    /// Restores the state captured before the current transaction began.
    /// </summary>
    public void Rollback()
    {
    }
}

internal sealed class TypeRegistryCoordinator
{
    private readonly object m_sync = new();
    private readonly List<RegistryReference> m_registries = [];

    internal TypeRegistryRegistration Register(ITypeRegistry registry)
    {
        lock (m_sync)
        {
            RemoveCollectedRegistries();
            var registration = new TypeRegistryRegistration(this, Guid.NewGuid());
            m_registries.Add(new RegistryReference(
                registration.id,
                new WeakReference<ITypeRegistry>(registry)));
            return registration;
        }
    }

    internal TypeRegistryRefreshSet Prepare(TypeCacheSnapshot types)
    {
        List<ITypeRegistry> registries = [];
        lock (m_sync)
        {
            RemoveCollectedRegistries();
            foreach (RegistryReference registration in m_registries)
            {
                if (registration.registry.TryGetTarget(out ITypeRegistry? registry))
                    registries.Add(registry);
            }
        }

        var transactions = new List<ITypeRegistryTransaction>(registries.Count);
        try
        {
            foreach (ITypeRegistry registry in registries)
                transactions.Add(registry.Prepare(types));
            return new TypeRegistryRefreshSet(transactions);
        }
        catch
        {
            for (int i = transactions.Count - 1; i >= 0; i--)
                TryCleanup(transactions[i].Rollback, "prepared registry rollback");
            throw;
        }
    }

    internal void Unregister(Guid registrationId)
    {
        lock (m_sync)
            m_registries.RemoveAll(registration => registration.id == registrationId);
    }

    private void RemoveCollectedRegistries()
        => m_registries.RemoveAll(static registration => !registration.registry.TryGetTarget(out _));

    private static void TryCleanup(Action cleanup, string phase)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            Trace.TraceError("Type registry {0} failed: {1}", phase, exception);
        }
    }

    private readonly record struct RegistryReference(
        Guid id,
        WeakReference<ITypeRegistry> registry);
}

internal sealed class TypeRegistryRegistration(
    TypeRegistryCoordinator owner,
    Guid id) : IDisposable
{
    private bool m_disposed;

    internal Guid id { get; } = id;

    /// <summary>
    /// Releases the resources owned by this instance.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        owner.Unregister(id);
    }
}

internal sealed class TypeRegistryRefreshSet(IReadOnlyList<ITypeRegistryTransaction> transactions)
{
    private int m_activatedCount;
    private bool m_finished;

    internal void Activate()
    {
        if (m_finished)
            throw new InvalidOperationException("Type registry refresh set is already finished.");
        try
        {
            for (; m_activatedCount < transactions.Count; m_activatedCount++)
                transactions[m_activatedCount].Activate();
        }
        catch
        {
            Rollback();
            throw;
        }
    }

    internal void Complete()
    {
        if (m_finished)
            return;
        for (int i = 0; i < transactions.Count; i++)
            TryCleanup(transactions[i].Complete, "transaction completion");
        m_finished = true;
        transactions = Array.Empty<ITypeRegistryTransaction>();
    }

    internal void Rollback()
    {
        if (m_finished)
            return;
        for (int i = transactions.Count - 1; i >= 0; i--)
            TryCleanup(transactions[i].Rollback, "transaction rollback");
        m_finished = true;
        transactions = Array.Empty<ITypeRegistryTransaction>();
    }

    private static void TryCleanup(Action cleanup, string phase)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            Trace.TraceError("Type registry {0} failed: {1}", phase, exception);
        }
    }
}
