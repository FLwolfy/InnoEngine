using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Execution;
using Inno.Extensibility.Reload;

namespace Inno.References;

/// <summary>
/// Coordinates preserved reference slots using the host generation publication and two-phase rollback protocol.
/// </summary>
/// <remarks>
/// The generation owner calls these phases at its safe point. This transaction never publishes types or
/// rolls back automatically: old structures must be restored before old converters, and old values after them.
/// </remarks>
public sealed class ReferenceRecoveryTransaction : IGenerationChange
{
    private readonly int m_ownerThread = Environment.CurrentManagedThreadId;
    private readonly IReferenceRecoveryParticipant[] m_participants;
    private readonly SerializedMissingState[] m_states;
    private ReferenceCatalog? m_catalog;
    private Phase m_phase;
    private int m_preparedCount;
    private Exception? m_retirementFailure;
    private IReadOnlyList<ReferenceRecoveryChange> m_changes = Array.Empty<ReferenceRecoveryChange>();

    /// <summary>
    /// Captures neutral slots and ordered domain owners without resolving or mutating live state.
    /// </summary>
    /// <param name="catalog">
    /// The candidate resolver set, consulted after provisional objects are registered.
    /// </param>
    /// <param name="missingStates">
    /// Preserved object and dependency slots, each with a unique owner key.
    /// </param>
    /// <param name="participants">
    /// Domain changes in dependency order, owned until commit or complete rollback.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// An input or collection entry is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A slot key is invalid or duplicated.
    /// </exception>
    public ReferenceRecoveryTransaction(
        ReferenceCatalog catalog,
        IEnumerable<SerializedMissingState> missingStates,
        IEnumerable<IReferenceRecoveryParticipant> participants)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(missingStates);
        ArgumentNullException.ThrowIfNull(participants);
        m_states = missingStates.ToArray();
        m_participants = participants.ToArray();
        var keys = new HashSet<ReferenceKey>();
        foreach (SerializedMissingState state in m_states)
        {
            ArgumentNullException.ThrowIfNull(state);
            if (state.key.ownerPersistentId == Guid.Empty || string.IsNullOrWhiteSpace(state.key.path) ||
                !keys.Add(state.key))
                throw new ArgumentException($"Invalid or duplicate recovery slot '{state.key}'.", nameof(missingStates));
        }
        foreach (IReferenceRecoveryParticipant participant in m_participants)
            ArgumentNullException.ThrowIfNull(participant);
        m_catalog = catalog;
    }

    /// <summary>
    /// Gets candidate resolutions after provisional application, or an empty set before resolution or after rollback.
    /// </summary>
    public IReadOnlyList<ReferenceRecoveryChange> changes => m_changes;

    /// <summary>
    /// Quiesces domain owners while the previous type and serialization publication is still active.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The phase is out of order or the caller is not the owner thread.
    /// </exception>
    public void PrepareForActivation()
    {
        RequirePhase(Phase.Captured);
        m_phase = Phase.Preparing;
        while (m_preparedCount < m_participants.Length)
        {
            IReferenceRecoveryParticipant participant = m_participants[m_preparedCount++];
            Invoke(participant.PrepareForActivation);
        }
        m_phase = Phase.Prepared;
    }

    /// <summary>
    /// Applies provisional objects and values, resolves preserved slots, and validates the candidate before commit.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Preparation is incomplete or the caller is not the owner thread.
    /// </exception>
    public void Apply()
    {
        RequirePhase(Phase.Prepared);
        m_phase = Phase.Applying;
        foreach (IReferenceRecoveryParticipant participant in m_participants)
            Invoke(participant.Apply);
        Invoke(() => m_changes = Array.AsReadOnly(m_states.Select(state =>
            new ReferenceRecoveryChange(state, m_catalog!.Resolve(state.descriptor))).ToArray()));
        foreach (IReferenceRecoveryParticipant participant in m_participants)
            Invoke(() => participant.Validate(m_changes));
        m_phase = Phase.Applied;
    }

    /// <summary>
    /// Retires previous objects after irreversible publication and releases every completed domain owner.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Application is incomplete or the caller is not the owner thread.
    /// </exception>
    /// <exception cref="AggregateException">
    /// One or more completed retirement operations failed.
    /// </exception>
    /// <exception cref="RetirementPendingException">
    /// Unfinished retirement retains all owners and blocks further phases.
    /// </exception>
    public void Complete()
    {
        RequirePhase(Phase.Applied);
        m_phase = Phase.Finalizing;
        RunTerminalPhase(restore: false);
    }

    /// <summary>
    /// Restores attempted domain structures in reverse order before the previous publication is restored.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The transaction is terminal or the caller is not the owner thread.
    /// </exception>
    /// <exception cref="AggregateException">
    /// One or more structure compensations failed.
    /// </exception>
    /// <exception cref="RetirementPendingException">
    /// An unfinished compensation retains its owners.
    /// </exception>
    public void RollbackStructure()
    {
        EnsureOwner();
        if (m_phase == Phase.RolledBack)
            return;
        if (m_phase is Phase.Finished or Phase.Finalizing or Phase.RollingBack)
            throw new InvalidOperationException("A completed recovery transaction cannot be rolled back.");
        m_phase = Phase.RollingBack;
        List<Exception> failures = [];
        for (int index = m_preparedCount - 1; index >= 0; index--)
            Attempt(m_participants[index].RollbackStructure, failures);
        m_phase = Phase.RolledBack;
        m_changes = Array.Empty<ReferenceRecoveryChange>();
        ThrowFailures(failures, "Reference recovery structure rollback failed.");
    }

    /// <summary>
    /// Restores previous values and lifecycle after the old type and serializer publication is active again.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Structure rollback is incomplete or the caller is not the owner thread.
    /// </exception>
    /// <exception cref="AggregateException">
    /// One or more previous-state restorations failed.
    /// </exception>
    /// <exception cref="RetirementPendingException">
    /// An unfinished restoration retains its owners.
    /// </exception>
    public void RestorePreviousState()
    {
        RequirePhase(Phase.RolledBack);
        m_phase = Phase.Finalizing;
        RunTerminalPhase(restore: true);
    }

    private void RunTerminalPhase(bool restore)
    {
        List<Exception> failures = [];
        if (restore)
        {
            for (int index = m_preparedCount - 1; index >= 0; index--)
                Attempt(m_participants[index].RestorePreviousState, failures);
        }
        else
        {
            foreach (IReferenceRecoveryParticipant participant in m_participants)
                Attempt(participant.Complete, failures);
        }
        m_phase = Phase.Finished;
        m_catalog = null;
        Array.Clear(m_participants);
        Array.Clear(m_states);
        ThrowFailures(failures, "Reference recovery finalization failed.");
    }

    private void Invoke(Action operation)
    {
        try { operation(); }
        catch (Exception failure) when (RetirementPendingException.Find(failure) is not null)
        {
            m_retirementFailure = failure;
            throw;
        }
    }

    private void Attempt(Action operation, ICollection<Exception> failures)
    {
        try { Invoke(operation); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null)
        {
            if (failures.Count > 0)
            {
                m_retirementFailure = new AggregateException("Reference recovery remains pending after earlier cleanup failures.", [.. failures, pendingRetirement]);
                throw m_retirementFailure;
            }
            throw;
        }
        catch (Exception exception) { failures.Add(exception); }
    }

    private static void ThrowFailures(List<Exception> failures, string message)
    {
        if (failures.Count > 0)
            throw new AggregateException(message, failures);
    }

    private void RequirePhase(Phase phase)
    {
        EnsureOwner();
        if (m_phase != phase)
            throw new InvalidOperationException($"Recovery phase is '{m_phase}', expected '{phase}'.");
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != m_ownerThread)
            throw new InvalidOperationException("Reference recovery must run on its capture thread.");
        if (m_retirementFailure is not null)
            throw m_retirementFailure;
    }

    private enum Phase { Captured, Preparing, Prepared, Applying, Applied, RollingBack, RolledBack, Finalizing, Finished }
}
