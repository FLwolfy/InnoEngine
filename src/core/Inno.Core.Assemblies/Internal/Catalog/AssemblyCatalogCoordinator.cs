using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Inno.Core.Assemblies.Internal;

internal static class AssemblyCatalogCoordinator
{
    private static readonly object S_SYNC = new();
    private static readonly List<ParticipantReference> S_PARTICIPANTS = [];

    internal static CatalogParticipantRegistration Register(IAssemblyCatalogParticipant participant)
    {
        lock (S_SYNC)
        {
            RemoveCollectedParticipants();
            var registration = new CatalogParticipantRegistration(Guid.NewGuid());
            S_PARTICIPANTS.Add(new ParticipantReference(
                registration.id,
                new WeakReference<IAssemblyCatalogParticipant>(participant)));
            return registration;
        }
    }

    internal static AssemblyCatalogRefreshSet Prepare(AssemblyCatalogSnapshot catalog)
    {
        List<IAssemblyCatalogParticipant> participants = [];
        lock (S_SYNC)
        {
            RemoveCollectedParticipants();
            foreach (ParticipantReference registration in S_PARTICIPANTS)
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

    internal static void Unregister(Guid registrationId)
    {
        lock (S_SYNC)
            S_PARTICIPANTS.RemoveAll(registration => registration.id == registrationId);
    }

    private static void RemoveCollectedParticipants()
        => S_PARTICIPANTS.RemoveAll(static registration => !registration.participant.TryGetTarget(out _));

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

internal sealed class CatalogParticipantRegistration(Guid id) : IDisposable
{
    private bool m_disposed;

    internal Guid id { get; } = id;

    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        AssemblyCatalogCoordinator.Unregister(id);
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
            Trace.TraceError("Assembly catalog {0} failed: {1}", phase, exception);
        }
    }
}
