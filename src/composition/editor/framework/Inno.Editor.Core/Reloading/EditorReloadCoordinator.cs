using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

using Inno.Extensibility.Modules;
using Inno.Core.Execution;
using Inno.Extensibility.Reload;
using Inno.Scripting.Reload;

namespace Inno.Editor.Core;

/// <summary>
/// Coordinates weakly registered editor feature migrations around atomic assembly reload sessions.
/// </summary>
public sealed class EditorReloadCoordinator : IScriptReloadCoordinator
{
    private readonly List<ParticipantReference> m_participants = [];
    private readonly object m_sync = new();

    /// <summary>
    /// Registers an editor feature that owns generation-bound live state.
    /// </summary>
    /// <param name="participant">
    /// The feature participant. The coordinator retains only a weak reference to this instance.
    /// </param>
    /// <returns>
    /// A registration lease that strongly retains the participant until the lease is disposed.
    /// The coordinator itself retains only a weak reference.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="participant"/> is <see langword="null"/>.
    /// </exception>
    public IDisposable Register(IEditorReloadParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        lock (m_sync)
        {
            RemoveCollectedParticipants();
            var registration = new Registration(this, Guid.NewGuid(), participant);
            m_participants.Add(new ParticipantReference(
                registration.id,
                new WeakReference<IEditorReloadParticipant>(participant)));
            return registration;
        }
    }

    /// <summary>
    /// Applies one prepared assembly reload together with every registered editor feature migration.
    /// </summary>
    /// <param name="reload">
    /// The prepared assembly reload session to activate and complete.
    /// </param>
    /// <param name="externalChange">
    /// Content recovery prepared before type publication, applied before Editor recovery, and restored before old values.
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
    public AssemblyUnloadMonitor Execute(
        AssemblyReloadSession reload,
        IGenerationChange? externalChange = null)
    {
        ArgumentNullException.ThrowIfNull(reload);
        IGenerationChange[] transactions = CaptureTransactions(reload.context);
        var publication = new EditorPublication(reload, externalChange);
        return reload.generations.Execute("editor assembly reload", publication, [publication, .. transactions]);
    }

    /// <summary>
    /// Requests every live participant to republish diagnostics derived from its current state.
    /// </summary>
    public void RefreshDiagnostics()
    {
        foreach (IEditorReloadParticipant participant in GetParticipants())
        {
            try
            {
                participant.RefreshDiagnostics();
            }
            catch (Exception pending) when (RetirementPendingException.Find(pending) is not null) { throw; }
            catch (Exception exception)
            {
                Trace.TraceError(
                    "Editor reload participant '{0}' failed to refresh diagnostics: {1}",
                    participant.GetType().FullName,
                    exception);
            }
        }
    }

    private IGenerationChange[] CaptureTransactions(AssemblyReloadContext context)
    {
        List<IEditorReloadParticipant> participants = GetParticipants();
        var transactions = new IGenerationChange[participants.Count];
        for (int i = 0; i < participants.Count; i++)
        {
            transactions[i] = new ParticipantChange(participants[i], context);
        }
        return transactions;
    }

    private List<IEditorReloadParticipant> GetParticipants()
    {
        var participants = new List<IEditorReloadParticipant>();
        lock (m_sync)
        {
            RemoveCollectedParticipants();
            foreach (ParticipantReference reference in m_participants)
            {
                if (reference.participant.TryGetTarget(out IEditorReloadParticipant? participant))
                    participants.Add(participant);
            }
        }
        return participants;
    }

    private void RemoveCollectedParticipants()
        => m_participants.RemoveAll(static reference => !reference.participant.TryGetTarget(out _));

    private sealed class EditorPublication(AssemblyReloadSession reload, IGenerationChange? external)
        : IGenerationPublication<AssemblyUnloadMonitor>, IGenerationChange
    {
        /// <summary>
        /// Publishes candidate assemblies before their external asset and settings generation.
        /// </summary>
        public void Activate()
        {
            reload.Activate();
            external?.Apply();
        }

        /// <summary>
        /// Commits the combined publication after all domain recovery participants succeeded.
        /// </summary>
        /// <returns>
        /// The weak monitor for the retired assembly generation.
        /// </returns>
        public AssemblyUnloadMonitor Complete() => reload.Complete();

        void IGenerationChange.PrepareForActivation() => external?.PrepareForActivation();
        void IGenerationChange.Apply() { }
        void IGenerationChange.Complete() => external?.Complete();
        void IGenerationChange.RollbackStructure() => external?.RollbackStructure();
        void IGenerationChange.RestorePreviousState() { }

        /// <summary>
        /// Restores old assemblies and external resolvers before domains restore their previous property values.
        /// </summary>
        public void Rollback()
        {
            List<Exception> failures = [];
            try { reload.Rollback(); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception) { failures.Add(exception); }
            try { external?.RestorePreviousState(); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception) { failures.Add(exception); }
            if (failures.Count > 0)
                throw new AggregateException("Editor publication rollback was incomplete.", failures);
        }
    }

    private sealed class ParticipantChange(IEditorReloadParticipant participant, AssemblyReloadContext context)
        : IGenerationChange
    {
        private IGenerationChange? m_change;
        /// <summary>
        /// Prepares candidate state without changing the active generation.
        /// </summary>
        public void PrepareForActivation()
        {
            m_change = participant.Capture(context)
                ?? throw new InvalidOperationException($"Editor reload participant '{participant.GetType().FullName}' returned no change.");
            m_change.PrepareForActivation();
        }
        /// <summary>
        /// Applies the prepared state at the caller-controlled commit point.
        /// </summary>
        public void Apply() => m_change!.Apply();
        /// <summary>
        /// Completes the committed operation and releases temporary state.
        /// </summary>
        public void Complete() { m_change!.Complete(); m_change = null; }
        /// <summary>
        /// Restores the state that existed before candidate activation began.
        /// </summary>
        public void RollbackStructure() => m_change?.RollbackStructure();
        /// <summary>
        /// Restores the state that existed before candidate activation began.
        /// </summary>
        public void RestorePreviousState() { m_change?.RestorePreviousState(); m_change = null; }
    }

    private void Unregister(Guid registrationId)
    {
        lock (m_sync)
            m_participants.RemoveAll(reference => reference.id == registrationId);
    }

    private readonly record struct ParticipantReference(
        Guid id,
        WeakReference<IEditorReloadParticipant> participant);

    private sealed class Registration : IDisposable
    {
        private readonly EditorReloadCoordinator m_owner;
        private IEditorReloadParticipant? m_participant;
        private bool m_disposed;

        internal Registration(
            EditorReloadCoordinator owner,
            Guid id,
            IEditorReloadParticipant participant)
        {
            m_owner = owner;
            this.id = id;
            m_participant = participant;
        }

        internal Guid id { get; }

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed)
                return;
            m_disposed = true;
            IEditorReloadParticipant? participant = m_participant;
            m_participant = null;
            m_owner.Unregister(id);
            GC.KeepAlive(participant);
        }
    }
}
