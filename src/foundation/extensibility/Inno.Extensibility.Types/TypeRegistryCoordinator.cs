using System;
using System.Collections.Generic;

using Inno.Core.Execution;

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
    private Exception? m_retirementFailure;
    private object? m_retainedPreparation;

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
        if (m_retirementFailure is not null)
        {
            GC.KeepAlive(m_retainedPreparation);
            throw m_retirementFailure;
        }
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
        catch (Exception failure) when (RetirementPendingException.Find(failure) is not null)
        {
            m_retirementFailure = failure;
            m_retainedPreparation = (registries, transactions);
            throw;
        }
        catch (Exception preparationFailure)
        {
            List<Exception> failures = [preparationFailure];
            for (int i = transactions.Count - 1; i >= 0; i--)
            {
                try { transactions[i].Rollback(); }
                catch (Exception failure) when (RetirementPendingException.Find(failure) is not null)
                {
                    m_retirementFailure = new AggregateException("Registry preparation failed and rollback remains pending.", [.. failures, failure]);
                    m_retainedPreparation = (registries, transactions);
                    throw m_retirementFailure;
                }
                catch (Exception cleanupFailure) { failures.Add(cleanupFailure); }
            }
            if (failures.Count > 1)
                throw new AggregateException("Type registry preparation and rollback failed.", failures);
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
    private Exception? m_failure;

    internal void Activate()
    {
        if (m_failure is not null)
            throw m_failure;
        if (m_finished)
            throw new InvalidOperationException("Type registry refresh set is already finished.");
        try
        {
            for (; m_activatedCount < transactions.Count; m_activatedCount++)
                transactions[m_activatedCount].Activate();
        }
        catch (Exception failure) when (RetirementPendingException.Find(failure) is not null)
        {
            m_failure = failure;
            throw;
        }
        catch (Exception activationFailure)
        {
            try { Rollback(); }
            catch (Exception rollbackFailure)
            {
                var combined = new AggregateException("Type registry activation and rollback failed.", activationFailure, rollbackFailure);
                if (RetirementPendingException.Find(combined) is not null)
                    m_failure = combined;
                throw combined;
            }
            throw;
        }
    }

    internal void Complete()
    {
        if (m_failure is not null)
            throw m_failure;
        if (m_finished)
            return;
        List<Exception> failures = [];
        for (int i = 0; i < transactions.Count; i++)
            TryCleanup(transactions[i].Complete, failures);
        m_finished = true;
        transactions = Array.Empty<ITypeRegistryTransaction>();
        if (failures.Count > 0)
            throw new AggregateException("Type registry completion failed after all participants were attempted.", failures);
    }

    internal void Rollback()
    {
        if (m_failure is not null)
            throw m_failure;
        if (m_finished)
            return;
        List<Exception> failures = [];
        for (int i = transactions.Count - 1; i >= 0; i--)
            TryCleanup(transactions[i].Rollback, failures);
        m_finished = true;
        transactions = Array.Empty<ITypeRegistryTransaction>();
        if (failures.Count > 0)
            throw new AggregateException("Type registry rollback failed after all participants were attempted.", failures);
    }

    private void TryCleanup(Action cleanup, List<Exception> failures)
    {
        try { cleanup(); }
        catch (Exception failure) when (RetirementPendingException.Find(failure) is not null)
        {
            m_failure = failures.Count == 0 ? failure : new AggregateException(
                "Registry cleanup remains pending after earlier failures.", [.. failures, failure]);
            if (ReferenceEquals(m_failure, failure)) throw;
            throw m_failure;
        }
        catch (Exception exception) { failures.Add(exception); }
    }
}
