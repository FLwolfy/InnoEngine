using System;
using System.Collections.Generic;

using Inno.Core.Execution;

namespace Inno.Extensibility.Modules.Internal;

internal sealed class AssemblyCatalogCoordinator
{
    private readonly object m_sync = new();
    private readonly List<ParticipantReference> m_participants = [];
    private Exception? m_retirementFailure;
    private object? m_retainedPreparation;

    internal CatalogParticipantRegistration Register(IAssemblyCatalogParticipant participant)
    {
        lock (m_sync)
        {
            RemoveCollectedParticipants();
            var registration = new CatalogParticipantRegistration(this, Guid.NewGuid());
            m_participants.Add(new ParticipantReference(
                registration.id,
                new WeakReference<IAssemblyCatalogParticipant>(participant)));
            return registration;
        }
    }

    internal AssemblyCatalogRefreshSet Prepare(AssemblyCatalogSnapshot catalog)
    {
        if (m_retirementFailure is not null)
        {
            GC.KeepAlive(m_retainedPreparation);
            throw m_retirementFailure;
        }
        List<IAssemblyCatalogParticipant> participants = [];
        lock (m_sync)
        {
            RemoveCollectedParticipants();
            foreach (ParticipantReference registration in m_participants)
            {
                if (registration.participant.TryGetTarget(out IAssemblyCatalogParticipant? participant))
                    participants.Add(participant);
            }
        }

        var transactions = new List<IAssemblyCatalogTransaction>(participants.Count);
        try
        {
            foreach (IAssemblyCatalogParticipant participant in participants)
                transactions.Add(participant.Prepare(catalog));
            return new AssemblyCatalogRefreshSet(transactions);
        }
        catch (Exception failure) when (RetirementPendingException.Find(failure) is not null)
        {
            m_retirementFailure = failure;
            m_retainedPreparation = (participants, transactions);
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
                    m_retirementFailure = new AggregateException("Catalog preparation failed and rollback remains pending.", [.. failures, failure]);
                    m_retainedPreparation = (participants, transactions);
                    throw m_retirementFailure;
                }
                catch (Exception cleanupFailure) { failures.Add(cleanupFailure); }
            }
            if (failures.Count > 1)
                throw new AggregateException("Assembly catalog preparation and rollback failed.", failures);
            throw;
        }
    }

    internal void Unregister(Guid registrationId)
    {
        lock (m_sync)
            m_participants.RemoveAll(registration => registration.id == registrationId);
    }

    private void RemoveCollectedParticipants()
        => m_participants.RemoveAll(static registration => !registration.participant.TryGetTarget(out _));

    private readonly record struct ParticipantReference(
        Guid id,
        WeakReference<IAssemblyCatalogParticipant> participant);
}

internal sealed class CatalogParticipantRegistration(
    AssemblyCatalogCoordinator owner,
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

internal sealed class AssemblyCatalogRefreshSet(IReadOnlyList<IAssemblyCatalogTransaction> transactions)
{
    private int m_activatedCount;
    private bool m_finished;
    private Exception? m_failure;

    internal IReadOnlyList<object> contexts
    {
        get
        {
            var result = new List<object>(transactions.Count);
            foreach (IAssemblyCatalogTransaction transaction in transactions)
            {
                if (transaction.context is not null)
                    result.Add(transaction.context);
            }
            return result;
        }
    }

    internal void Activate()
    {
        if (m_failure is not null)
            throw m_failure;
        if (m_finished)
            throw new InvalidOperationException("Assembly catalog refresh set is already finished.");
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
                var combined = new AggregateException("Assembly catalog activation and rollback failed.", activationFailure, rollbackFailure);
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
        transactions = Array.Empty<IAssemblyCatalogTransaction>();
        if (failures.Count > 0)
            throw new AggregateException("Assembly catalog completion failed after all participants were attempted.", failures);
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
        transactions = Array.Empty<IAssemblyCatalogTransaction>();
        if (failures.Count > 0)
            throw new AggregateException("Assembly catalog rollback failed after all participants were attempted.", failures);
    }

    private void TryCleanup(Action cleanup, List<Exception> failures)
    {
        try { cleanup(); }
        catch (Exception failure) when (RetirementPendingException.Find(failure) is not null)
        {
            m_failure = failures.Count == 0 ? failure : new AggregateException(
                "Catalog cleanup remains pending after earlier failures.", [.. failures, failure]);
            if (ReferenceEquals(m_failure, failure)) throw;
            throw m_failure;
        }
        catch (Exception exception) { failures.Add(exception); }
    }
}
