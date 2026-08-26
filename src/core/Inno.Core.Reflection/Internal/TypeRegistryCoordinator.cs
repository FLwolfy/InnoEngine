using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Inno.Core.Reflection.Internal;

internal interface ITypeRegistry
{
    ITypeRegistryTransaction Prepare(TypeCacheSnapshot types);
}

internal interface ITypeRegistryTransaction
{
    void Activate();
    void Complete();
    void Rollback();
}

internal sealed class TypeRegistryNoopTransaction : ITypeRegistryTransaction
{
    internal static TypeRegistryNoopTransaction instance { get; } = new();

    public void Activate()
    {
    }

    public void Complete()
    {
    }

    public void Rollback()
    {
    }
}

internal static class TypeRegistryCoordinator
{
    private static readonly object S_SYNC = new();
    private static readonly List<RegistryReference> S_REGISTRIES = [];

    internal static TypeRegistryRegistration Register(ITypeRegistry registry)
    {
        lock (S_SYNC)
        {
            RemoveCollectedRegistries();
            var registration = new TypeRegistryRegistration(Guid.NewGuid());
            S_REGISTRIES.Add(new RegistryReference(
                registration.id,
                new WeakReference<ITypeRegistry>(registry)));
            return registration;
        }
    }

    internal static TypeRegistryRefreshSet Prepare(TypeCacheSnapshot types)
    {
        List<ITypeRegistry> registries = [];
        lock (S_SYNC)
        {
            RemoveCollectedRegistries();
            foreach (RegistryReference registration in S_REGISTRIES)
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

    internal static void Unregister(Guid registrationId)
    {
        lock (S_SYNC)
            S_REGISTRIES.RemoveAll(registration => registration.id == registrationId);
    }

    private static void RemoveCollectedRegistries()
        => S_REGISTRIES.RemoveAll(static registration => !registration.registry.TryGetTarget(out _));

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

internal sealed class TypeRegistryRegistration(Guid id) : IDisposable
{
    private bool m_disposed;

    internal Guid id { get; } = id;

    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        TypeRegistryCoordinator.Unregister(id);
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
    }

    internal void Rollback()
    {
        if (m_finished)
            return;
        for (int i = transactions.Count - 1; i >= 0; i--)
            TryCleanup(transactions[i].Rollback, "transaction rollback");
        m_finished = true;
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
