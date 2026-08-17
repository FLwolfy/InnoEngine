using System;
using System.Collections.Generic;

using Inno.Core.Reflection;

namespace Inno.Core.Assemblies.Internal;

internal interface ITypeRegistry
{
    IRegistryRefreshTransaction Prepare(TypeCacheSnapshot types);
}

internal interface IRegistryRefreshTransaction
{
    void Activate();
    void Complete();
    void Rollback();
}

internal sealed class RegistryRefreshTransaction : IRegistryRefreshTransaction
{
    internal static RegistryRefreshTransaction noop { get; } = new();

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

internal static class RegistryCoordinator
{
    private static readonly object S_SYNC = new();
    private static readonly List<RegistryReference> S_REGISTRIES = [];

    internal static RegistryRegistration Register(ITypeRegistry registry)
    {
        lock (S_SYNC)
        {
            RemoveCollectedRegistries();
            var registration = new RegistryRegistration(Guid.NewGuid());
            S_REGISTRIES.Add(new RegistryReference(
                registration.id,
                new WeakReference<ITypeRegistry>(registry)));
            return registration;
        }
    }

    internal static RegistryRefreshSet Prepare(TypeCacheSnapshot types)
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

        var transactions = new List<IRegistryRefreshTransaction>(registries.Count);
        try
        {
            foreach (ITypeRegistry registry in registries)
                transactions.Add(registry.Prepare(types));
            return new RegistryRefreshSet(transactions);
        }
        catch
        {
            for (int i = transactions.Count - 1; i >= 0; i--)
                transactions[i].Rollback();
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

    private readonly record struct RegistryReference(
        Guid id,
        WeakReference<ITypeRegistry> registry);
}

internal sealed class RegistryRegistration(Guid id) : IDisposable
{
    private bool m_disposed;

    internal Guid id { get; } = id;

    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        RegistryCoordinator.Unregister(id);
    }
}

internal sealed class RegistryRefreshSet(IReadOnlyList<IRegistryRefreshTransaction> transactions)
{
    private int m_activatedCount;
    private bool m_finished;

    internal void Activate()
    {
        if (m_finished)
            throw new InvalidOperationException("Registry refresh set is already finished.");
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
            transactions[i].Complete();
        m_finished = true;
    }

    internal void Rollback()
    {
        if (m_finished)
            return;
        for (int i = transactions.Count - 1; i >= 0; i--)
            transactions[i].Rollback();
        m_finished = true;
    }
}
