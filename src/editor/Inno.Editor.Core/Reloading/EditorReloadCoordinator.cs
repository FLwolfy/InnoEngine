using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

using Inno.Core.Assemblies;

namespace Inno.Editor.Core;

/// <summary>
/// Coordinates weakly registered editor feature migrations around atomic assembly reload sessions.
/// </summary>
public static class EditorReloadCoordinator
{
    private static readonly List<ParticipantReference> S_PARTICIPANTS = [];
    private static readonly object S_SYNC = new();

    /// <summary>
    /// Registers an editor feature that owns generation-bound live state.
    /// </summary>
    /// <param name="participant">
    /// The feature participant. The coordinator retains only a weak reference to this instance.
    /// </param>
    /// <returns>
    /// A registration that must be disposed when the feature stops participating in reloads.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="participant"/> is <see langword="null"/>.
    /// </exception>
    public static IDisposable Register(IEditorReloadParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        lock (S_SYNC)
        {
            RemoveCollectedParticipants();
            var registration = new Registration(Guid.NewGuid());
            S_PARTICIPANTS.Add(new ParticipantReference(
                registration.id,
                new WeakReference<IEditorReloadParticipant>(participant)));
            return registration;
        }
    }

    /// <summary>
    /// Applies one prepared assembly reload together with every registered editor feature migration.
    /// </summary>
    /// <param name="reload">The prepared assembly reload session to activate and complete.</param>
    /// <param name="activateExternalCandidate">
    /// Optional synchronization performed after candidate assembly activation, such as provisionally
    /// activating a staged Asset or Plugin generation.
    /// </param>
    /// <param name="restoreExternalState">
    /// Optional synchronization performed after assembly rollback to restore the previous external generation.
    /// </param>
    /// <returns>
    /// A monitor that observes cooperative unloading of assemblies retired by the committed reload.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="reload"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="AggregateException">
    /// Thrown when activation fails and one or more feature or assembly rollback stages also fail.
    /// </exception>
    public static AssemblyUnloadMonitor Execute(
        AssemblyReloadSession reload,
        Action? activateExternalCandidate = null,
        Action? restoreExternalState = null)
    {
        ArgumentNullException.ThrowIfNull(reload);
        IEditorReloadTransaction[] transactions = CaptureTransactions(reload.context);
        try
        {
            for (int i = 0; i < transactions.Length; i++)
                transactions[i].PrepareForActivation();
            reload.Activate();
            activateExternalCandidate?.Invoke();
            for (int i = 0; i < transactions.Length; i++)
                transactions[i].Apply();
            AssemblyUnloadMonitor monitor = reload.Complete();
            for (int i = 0; i < transactions.Length; i++)
                CompleteSafely(transactions[i]);
            return monitor;
        }
        catch (Exception exception)
        {
            var rollbackFailures = new List<Exception>();
            for (int i = transactions.Length - 1; i >= 0; i--)
            {
                TryRollback(
                    transactions[i].RollbackStructure,
                    "feature structure rollback",
                    rollbackFailures);
            }
            TryRollback(reload.Rollback, "assembly generation rollback", rollbackFailures);
            if (restoreExternalState is not null)
            {
                TryRollback(
                    restoreExternalState,
                    "external state rollback synchronization",
                    rollbackFailures);
            }
            for (int i = transactions.Length - 1; i >= 0; i--)
            {
                TryRollback(
                    transactions[i].RestorePreviousState,
                    "feature state restoration",
                    rollbackFailures);
            }
            if (rollbackFailures.Count == 0)
                ExceptionDispatchInfo.Capture(exception).Throw();
            throw new AggregateException(
                "Editor assembly reload failed and one or more rollback stages also failed.",
                [exception, .. rollbackFailures]);
        }
    }

    /// <summary>
    /// Requests every live participant to republish diagnostics derived from its current state.
    /// </summary>
    public static void RefreshDiagnostics()
    {
        foreach (IEditorReloadParticipant participant in GetParticipants())
        {
            try
            {
                participant.RefreshDiagnostics();
            }
            catch (Exception exception)
            {
                Trace.TraceError(
                    "Editor reload participant '{0}' failed to refresh diagnostics: {1}",
                    participant.GetType().FullName,
                    exception);
            }
        }
    }

    private static IEditorReloadTransaction[] CaptureTransactions(AssemblyReloadContext context)
    {
        List<IEditorReloadParticipant> participants = GetParticipants();
        var transactions = new IEditorReloadTransaction[participants.Count];
        for (int i = 0; i < participants.Count; i++)
        {
            transactions[i] = participants[i].Capture(context)
                ?? throw new InvalidOperationException(
                    $"Editor reload participant '{participants[i].GetType().FullName}' returned a null transaction.");
        }
        return transactions;
    }

    private static List<IEditorReloadParticipant> GetParticipants()
    {
        var participants = new List<IEditorReloadParticipant>();
        lock (S_SYNC)
        {
            RemoveCollectedParticipants();
            foreach (ParticipantReference reference in S_PARTICIPANTS)
            {
                if (reference.participant.TryGetTarget(out IEditorReloadParticipant? participant))
                    participants.Add(participant);
            }
        }
        return participants;
    }

    private static void RemoveCollectedParticipants()
        => S_PARTICIPANTS.RemoveAll(static reference => !reference.participant.TryGetTarget(out _));

    private static void CompleteSafely(IEditorReloadTransaction transaction)
    {
        try
        {
            transaction.Complete();
        }
        catch (Exception exception)
        {
            Trace.TraceError(
                "Editor reload transaction '{0}' failed during post-publication cleanup: {1}",
                transaction.GetType().FullName,
                exception);
        }
    }

    private static void TryRollback(
        Action rollback,
        string stage,
        ICollection<Exception> failures)
    {
        try
        {
            rollback();
        }
        catch (Exception exception)
        {
            failures.Add(new InvalidOperationException(
                $"Editor reload {stage} failed.",
                exception));
        }
    }

    private static void Unregister(Guid registrationId)
    {
        lock (S_SYNC)
            S_PARTICIPANTS.RemoveAll(reference => reference.id == registrationId);
    }

    private readonly record struct ParticipantReference(
        Guid id,
        WeakReference<IEditorReloadParticipant> participant);

    private sealed class Registration(Guid id) : IDisposable
    {
        private bool m_disposed;

        internal Guid id { get; } = id;

        public void Dispose()
        {
            if (m_disposed)
                return;
            m_disposed = true;
            Unregister(id);
        }
    }
}
