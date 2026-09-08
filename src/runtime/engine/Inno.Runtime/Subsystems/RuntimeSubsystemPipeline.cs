using Inno.Runtime.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Diagnostics;
using Inno.Core.Execution;
using Inno.Extensibility.Reload;

namespace Inno.Runtime;

/// <summary>
/// Owns the dependency-ordered runtime subsystem generation for one Host or Session scope.
/// </summary>
public sealed class RuntimeSubsystemPipeline : IDisposable
{
    private readonly List<Entry> m_entries = [];
    private readonly LifetimeScope m_resources;
    private readonly RuntimeSubsystemContext m_context;
    private readonly List<Exception> m_retirementFailures = [];
    private readonly List<Diagnostic> m_startupDiagnostics = [];
    private DiagnosticReporter? m_reporter;
    private readonly TimeSpan m_retirementTimeout;
    private readonly GenerationCoordinator m_generations;
    private int m_begunEntryCount;
    private bool m_frameOpen;
    private bool m_disposed;
    private bool m_stopping;
    private bool m_started;

    /// <summary>
    /// Acquires construction ownership before any factory or attachment callback runs.
    /// </summary>
    /// <param name="context">
    /// Foundation services and resources whose lifetime transfers to this pipeline.
    /// </param>
    /// <param name="retirementTimeout">
    /// The maximum candidate compensation duration.
    /// </param>
    /// <param name="generations">
    /// The shared gate faulted by irreversible retirement failure.
    /// </param>
    internal RuntimeSubsystemPipeline(RuntimeSubsystemContext context, TimeSpan retirementTimeout,
        GenerationCoordinator generations)
    {
        ArgumentNullException.ThrowIfNull(context);
        m_context = context;
        m_resources = context.resources;
        m_retirementTimeout = retirementTimeout;
        m_generations = generations;
    }

    internal void Start(IReadOnlyList<IRuntimeSubsystemFactory> configuredFactories)
    {
        ArgumentNullException.ThrowIfNull(configuredFactories);
        try
        {
            IRuntimeSubsystemFactory[] ordered = Order(configuredFactories.Select(static factory =>
                (IRuntimeSubsystemFactory)new FactorySnapshot(factory)).ToArray());
            if (ordered.Any(factory => factory.descriptor.lifetime != m_context.lifetime))
                throw new InvalidOperationException($"Subsystem lifetime does not match its {m_context.lifetime} owner.");
            m_reporter = m_resources.Own(m_context.diagnostics.CreateReporter(new DiagnosticSource(
                $"inno.runtime.subsystems.{m_context.identities.domainId}", "Runtime subsystems")));
            foreach (IRuntimeSubsystemFactory factory in ordered)
            {
                RuntimeSubsystemDescriptor descriptor = factory.descriptor;
                string? unavailable = UnavailableReason(descriptor);
                if (unavailable is not null)
                {
                    RejectOrReport(descriptor, unavailable);
                    continue;
                }
                var entry = new Entry(descriptor);
                m_entries.Add(entry);
                try
                {
                    var context = new RuntimeSubsystemContext(m_context.events, m_context.diagnostics,
                        m_context.identities, m_context.types, entry.resources, m_context.lifetime,
                        m_context.persistentDataDirectory, m_context.isEditMode, m_context.capabilities);
                    entry.subsystem = factory.Create(context)
                        ?? throw new InvalidOperationException($"Runtime subsystem factory '{descriptor.id}' returned null.");
                    entry.subsystem.Attach();
                    entry.isAttached = true;
                }
                catch (Exception failure) when (descriptor.requirement == RuntimeSubsystemRequirement.Optional
                                                && failure is not RetirementPendingException
                                                && m_generations.state != GenerationState.Faulted)
                {
                    // Optional startup can degrade only after the exact candidate owner has retired completely.
                    try
                    {
                        new RetirementBarrier($"Optional subsystem {descriptor.id}", m_retirementTimeout)
                            .Wait(() => RetireEntry(entry));
                    }
                    catch (Exception cleanupFailure)
                    {
                        m_generations.Fault(new AggregateException(
                            $"Optional subsystem '{descriptor.id}' could not compensate startup.", failure, cleanupFailure));
                        throw;
                    }
                    m_entries.RemoveAt(m_entries.Count - 1);
                    RejectOrReport(descriptor, $"Startup failed: {failure.Message}");
                }
            }
            m_started = true;
        }
        catch
        {
            m_stopping = true;
            throw;
        }
    }

    /// <summary>
    /// Gets the active immutable subsystem descriptors in execution order.
    /// </summary>
    public IReadOnlyList<RuntimeSubsystemDescriptor> descriptors
        => m_entries.Select(static entry => entry.descriptor).ToArray();

    /// <summary>
    /// Gets immutable diagnostics for unavailable optional subsystems; no exception or extension instance is retained.
    /// </summary>
    public IReadOnlyList<Diagnostic> startupDiagnostics => Array.AsReadOnly(m_startupDiagnostics.ToArray());

    /// <summary>
    /// Resolves the unique active subsystem that implements the requested contract.
    /// </summary>
    /// <typeparam name="TFeature">
    /// The subsystem contract or concrete subsystem type required by the composition root.
    /// </typeparam>
    /// <returns>
    /// The unique compatible subsystem owned by this pipeline.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no compatible subsystem exists or more than one compatible subsystem is active.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after this pipeline has been disposed.
    /// </exception>
    public TFeature GetRequiredSubsystem<TFeature>()
        where TFeature : class, IRuntimeSubsystem
    {
        EnsureActive();
        TFeature[] matches = m_entries
            .Select(static entry => entry.subsystem)
            .OfType<TFeature>()
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"No runtime subsystem implements '{typeof(TFeature).FullName}'."),
            _ => throw new InvalidOperationException(
                $"Multiple runtime subsystems implement '{typeof(TFeature).FullName}'.")
        };
    }

    /// <summary>
    /// Opens foundation scopes and immutable snapshots for one owner frame.
    /// </summary>
    /// <param name="frame">
    /// The frame timing snapshot.
    /// </param>
    public void BeginFrame(RuntimeFrame frame)
    {
        EnsureActive();
        if (m_frameOpen)
            throw new InvalidOperationException("A runtime subsystem frame is already active.");
        m_frameOpen = true;
        m_begunEntryCount = 0;
        for (int index = 0; index < m_entries.Count; index++)
        {
            m_entries[index].subsystem.BeginFrame(frame);
            m_begunEntryCount++;
        }
    }

    /// <summary>
    /// Advances every subsystem by one deterministic fixed step.
    /// </summary>
    /// <param name="frame">
    /// The fixed-step timing snapshot.
    /// </param>
    public void FixedUpdate(RuntimeFixedFrame frame)
    {
        EnsureFrame();
        ExecuteForward(static (subsystem, state) => subsystem.FixedUpdate(state), frame);
    }

    /// <summary>
    /// Advances variable-clock domain state.
    /// </summary>
    /// <param name="frame">
    /// The frame timing snapshot.
    /// </param>
    public void Update(RuntimeFrame frame)
    {
        EnsureFrame();
        ExecuteForward(static (subsystem, state) => subsystem.Update(state), frame);
    }

    /// <summary>
    /// Advances state that depends on completed simulation.
    /// </summary>
    /// <param name="frame">
    /// The frame timing snapshot.
    /// </param>
    public void LateUpdate(RuntimeFrame frame)
    {
        EnsureFrame();
        ExecuteForward(static (subsystem, state) => subsystem.LateUpdate(state), frame);
    }

    /// <summary>
    /// Opens output resources, accepts product requests and submits each owner's output once.
    /// </summary>
    /// <param name="frame">
    /// The frame timing snapshot.
    /// </param>
    /// <param name="submit">
    /// Optional control-thread presentation code that submits requests while output is open.
    /// </param>
    public void RenderFrame(RuntimeFrame frame, Action? submit = null)
    {
        EnsureFrame();
        int preparedCount = 0;
        List<Exception> failures = [];
        try
        {
            for (int index = 0; index < m_entries.Count; index++)
            {
                m_entries[index].subsystem.BeforeRender(frame);
                preparedCount++;
            }
            submit?.Invoke();
            ExecuteForward(static (subsystem, state) => subsystem.Render(state), frame);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
        try
        {
            ExecuteReverse(
                static (subsystem, state) => subsystem.AfterRender(state),
                frame,
                preparedCount);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
        if (failures.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1)
            throw new AggregateException("Subsystem output and cleanup failed.", failures);
    }

    /// <summary>
    /// Closes every successfully opened frame scope in reverse dependency order.
    /// </summary>
    /// <param name="frame">
    /// The frame being closed.
    /// </param>
    public void EndFrame(RuntimeFrame frame)
    {
        if (!m_frameOpen)
            return;
        try
        {
            ExecuteReverse(
                static (subsystem, state) => subsystem.EndFrame(state),
                frame,
                m_begunEntryCount);
        }
        finally
        {
            m_begunEntryCount = 0;
            m_frameOpen = false;
        }
    }

    /// <summary>
    /// Detaches and disposes every subsystem in reverse dependency order.
    /// </summary>
    /// <exception cref="AggregateException">
    /// Thrown after every release stage is attempted when one or more subsystems fail to detach or dispose.
    /// </exception>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_stopping = true;
        m_frameOpen = false;
        Release();
        try { m_resources.Dispose(); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception exception) { m_retirementFailures.Add(exception); }
        m_disposed = true;
        if (m_retirementFailures.Count > 0)
        {
            var failure = new AggregateException("Subsystem pipeline retirement failed.", m_retirementFailures);
            m_retirementFailures.Clear();
            throw failure;
        }
    }

    private static IRuntimeSubsystemFactory[] Order(IReadOnlyList<IRuntimeSubsystemFactory> factories)
    {
        var byId = new Dictionary<RuntimeSubsystemId, IRuntimeSubsystemFactory>();
        foreach (IRuntimeSubsystemFactory factory in factories)
        {
            ArgumentNullException.ThrowIfNull(factory);
            RuntimeSubsystemDescriptor descriptor = factory.descriptor
                ?? throw new InvalidOperationException("A runtime subsystem factory returned a null descriptor.");
            if (!byId.TryAdd(descriptor.id, factory))
                throw new InvalidOperationException($"Runtime subsystem ID '{descriptor.id}' is duplicated.");
        }
        foreach (IRuntimeSubsystemFactory factory in factories)
        {
            foreach (RuntimeSubsystemId dependency in factory.descriptor.dependencies)
            {
                if (!byId.ContainsKey(dependency) && factory.descriptor.requirement == RuntimeSubsystemRequirement.Required)
                {
                    throw new InvalidOperationException(
                        $"Runtime subsystem '{factory.descriptor.id}' requires missing subsystem '{dependency}'.");
                }
            }
        }

        var incoming = factories.ToDictionary(
            static factory => factory.descriptor.id,
            factory => factory.descriptor.dependencies.Count(byId.ContainsKey));
        var dependants = factories.ToDictionary(
            static factory => factory.descriptor.id,
            static _ => new List<IRuntimeSubsystemFactory>());
        foreach (IRuntimeSubsystemFactory factory in factories)
        {
            foreach (RuntimeSubsystemId dependency in factory.descriptor.dependencies)
                if (dependants.TryGetValue(dependency, out List<IRuntimeSubsystemFactory>? consumers))
                    consumers.Add(factory);
        }
        var ready = new List<IRuntimeSubsystemFactory>(
            factories.Where(factory => incoming[factory.descriptor.id] == 0));
        var result = new List<IRuntimeSubsystemFactory>(factories.Count);
        while (ready.Count > 0)
        {
            ready.Sort(CompareFactories);
            IRuntimeSubsystemFactory next = ready[0];
            ready.RemoveAt(0);
            result.Add(next);
            foreach (IRuntimeSubsystemFactory dependant in dependants[next.descriptor.id])
            {
                int remaining = --incoming[dependant.descriptor.id];
                if (remaining == 0)
                    ready.Add(dependant);
            }
        }
        if (result.Count != factories.Count)
        {
            string unresolved = string.Join(
                ", ",
                incoming.Where(static pair => pair.Value > 0)
                    .Select(static pair => pair.Key.value)
                    .Order(StringComparer.Ordinal));
            throw new InvalidOperationException($"Runtime subsystem dependencies contain a cycle: {unresolved}.");
        }
        return result.ToArray();
    }

    private static int CompareFactories(IRuntimeSubsystemFactory left, IRuntimeSubsystemFactory right)
    {
        int order = left.descriptor.order.CompareTo(right.descriptor.order);
        return order != 0
            ? order
            : StringComparer.Ordinal.Compare(left.descriptor.id.value, right.descriptor.id.value);
    }

    private void Release()
    {
        while (m_entries.Count > 0)
        {
            Entry entry = m_entries[^1];
            try { RetireEntry(entry); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception)
            {
                m_retirementFailures.Add(exception);
            }
            m_entries.RemoveAt(m_entries.Count - 1);
        }
    }

    private static void RetireEntry(Entry entry)
    {
        if (entry.isAttached)
        {
            try { entry.subsystem.Detach(); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception) { entry.failures.Add(exception); }
            entry.isAttached = false;
        }
        if (entry.subsystem is not null)
        {
            try { entry.subsystem.Dispose(); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception) { entry.failures.Add(exception); }
            entry.subsystem = null!;
        }
        try { entry.resources.Dispose(); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception exception) { entry.failures.Add(exception); }
        if (entry.failures.Count > 0)
        {
            var failure = new AggregateException($"Subsystem '{entry.descriptor.id}' failed to retire.", entry.failures);
            entry.failures.Clear();
            throw failure;
        }
    }

    private string? UnavailableReason(RuntimeSubsystemDescriptor descriptor)
    {
        RuntimeCapabilityId[] missing = descriptor.requiredCapabilities.Where(id => !m_context.capabilities.Contains(id)).ToArray();
        if (missing.Length > 0)
            return $"Missing capabilities: {string.Join(", ", missing)}.";
        RuntimeSubsystemId[] dependencies = descriptor.dependencies
            .Where(id => !m_entries.Any(entry => entry.descriptor.id == id && entry.isAttached)).ToArray();
        return dependencies.Length == 0 ? null : $"Unavailable dependencies: {string.Join(", ", dependencies)}.";
    }

    private void RejectOrReport(RuntimeSubsystemDescriptor descriptor, string reason)
    {
        if (descriptor.requirement == RuntimeSubsystemRequirement.Required)
            throw new InvalidOperationException($"Required subsystem '{descriptor.id}' cannot start: {reason}");
        var diagnostic = new Diagnostic("runtime.subsystem.unavailable", reason, DiagnosticSeverity.Warning,
            semanticId: descriptor.id.value);
        m_startupDiagnostics.Add(diagnostic);
        m_reporter!.Publish(diagnostic);
    }

    private void ExecuteForward<TState>(Action<IRuntimeSubsystem, TState> action, TState state)
    {
        foreach (Entry entry in m_entries)
            action(entry.subsystem, state);
    }

    private void ExecuteReverse<TState>(
        Action<IRuntimeSubsystem, TState> action,
        TState state,
        int count)
    {
        List<Exception>? failures = null;
        for (int index = count - 1; index >= 0; index--)
        {
            try
            {
                action(m_entries[index].subsystem, state);
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }
        if (failures is not null)
            throw new AggregateException("One or more runtime subsystem phases failed.", failures);
    }

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_stopping)
            throw new InvalidOperationException("The subsystem pipeline is retiring and cannot execute new work.");
        if (!m_started)
            throw new InvalidOperationException("The subsystem pipeline has not finished startup.");
    }

    private void EnsureFrame()
    {
        EnsureActive();
        if (!m_frameOpen)
            throw new InvalidOperationException("No runtime subsystem frame is active.");
    }

    private sealed class Entry(RuntimeSubsystemDescriptor descriptor)
    {
        internal RuntimeSubsystemDescriptor descriptor { get; } = descriptor;

        internal IRuntimeSubsystem subsystem { get; set; } = null!;

        internal LifetimeScope resources { get; } = new();

        internal List<Exception> failures { get; } = [];

        internal bool isAttached { get; set; }
    }

    private sealed class FactorySnapshot : IRuntimeSubsystemFactory
    {
        private readonly IRuntimeSubsystemFactory m_factory;

        internal FactorySnapshot(IRuntimeSubsystemFactory factory)
        {
            m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
            descriptor = factory.descriptor ?? throw new InvalidOperationException("A subsystem descriptor cannot be null.");
        }

        /// <summary>
        /// Gets the descriptor captured before any factory executes.
        /// </summary>
        public RuntimeSubsystemDescriptor descriptor { get; }

        /// <summary>
        /// Invokes the configured factory inside its separately owned construction lifetime.
        /// </summary>
        /// <param name="context">
        /// The pipeline-provided foundation services and private lifetime.
        /// </param>
        /// <returns>
        /// The new unattached subsystem; a null result is rejected by the pipeline.
        /// </returns>
        public IRuntimeSubsystem Create(RuntimeSubsystemContext context) => m_factory.Create(context);
    }
}
