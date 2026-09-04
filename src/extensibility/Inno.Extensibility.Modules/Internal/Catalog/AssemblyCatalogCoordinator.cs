using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Inno.Extensibility.Modules.Internal;

internal sealed class AssemblyCatalogCoordinator
{
    private readonly object m_sync = new();
    private readonly List<ParticipantReference> m_participants = [];

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
        catch
        {
            for (int i = transactions.Count - 1; i >= 0; i--)
                TryCleanup(transactions[i].Rollback, "prepared transaction rollback");
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

    private static void TryCleanup(Action cleanup, string phase)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            Trace.TraceError("Assembly catalog {0} failed: {1}", phase, exception);
        }
    }

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
        if (m_finished)
            throw new InvalidOperationException("Assembly catalog refresh set is already finished.");
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
        transactions = Array.Empty<IAssemblyCatalogTransaction>();
    }

    internal void Rollback()
    {
        if (m_finished)
            return;
        for (int i = transactions.Count - 1; i >= 0; i--)
            TryCleanup(transactions[i].Rollback, "transaction rollback");
        m_finished = true;
        transactions = Array.Empty<IAssemblyCatalogTransaction>();
    }

    private static void TryCleanup(Action cleanup, string phase)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            Trace.TraceError("Assembly catalog {0} failed: {1}", phase, exception);
        }
    }
}
