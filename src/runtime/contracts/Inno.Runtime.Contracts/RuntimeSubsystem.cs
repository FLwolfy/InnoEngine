using System;
using System.Collections.Generic;
using Inno.Core.Execution;

namespace Inno.Runtime.Contracts;

/// <summary>
/// Enforces one control-thread lifecycle while derived runtimes compose their own domain services.
/// </summary>
public abstract class RuntimeSubsystem : IRuntimeSubsystem
{
    private readonly List<IDisposable> m_frameScopes = [];
    private readonly LifetimeScope m_lifetime = new();
    private readonly List<Exception> m_retirementFailures = [];
    private bool m_started;
    private bool m_frameOpen;
    private bool m_disposed;
    private bool m_stopping;
    private bool m_stopCompleted;
    private int m_ownerThread;

    /// <summary>
    /// Gets whether this instance is attached to an owner and has not retired.
    /// </summary>
    public bool isStarted => m_started && !m_disposed && !m_stopping;

    /// <summary>
    /// Gets resources and cancellation owned exclusively by this subsystem.
    /// </summary>
    protected LifetimeScope lifetime => m_lifetime;
    /// <summary>
    /// Acquires session resources after every dependency has attached.
    /// </summary>
    /// <remarks>
    /// The caller must own this instance before attachment. Failed attachment stops admission but leaves partial
    /// resources with that owner, which must drive Dispose through any pending retirement.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The subsystem is already started.
    /// </exception>
    public void Attach()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_started)
            throw new InvalidOperationException("A subsystem cannot be attached to multiple owners.");
        m_ownerThread = Environment.CurrentManagedThreadId;
        m_started = true;
        try { OnStart(); }
        catch
        {
            // The enclosing owner was registered before Attach and retains this partial instance.
            m_stopping = true;
            throw;
        }
    }
    /// <summary>
    /// Begins one frame before event dispatch and simulation.
    /// </summary>
    /// <param name="frame">
    /// The immutable variable-frame state.
    /// </param>
    public void BeginFrame(RuntimeFrame frame)
    {
        EnsureStarted();
        if (m_frameOpen)
            throw new InvalidOperationException("A subsystem frame is already open.");
        m_frameOpen = true;
        try { OnBeginFrame(frame); }
        catch (Exception failure)
        {
            try { EndFrame(frame); }
            catch (Exception cleanup) { throw new AggregateException("Subsystem frame entry and rollback failed.", failure, cleanup); }
            throw;
        }
    }
    /// <summary>
    /// Advances one deterministic simulation step.
    /// </summary>
    /// <param name="frame">
    /// The immutable fixed-step state.
    /// </param>
    public void FixedUpdate(RuntimeFixedFrame frame) { EnsureFrame(); OnFixedUpdate(frame); }
    /// <summary>
    /// Advances variable simulation state.
    /// </summary>
    /// <param name="frame">
    /// The immutable variable-frame state.
    /// </param>
    public void Update(RuntimeFrame frame) { EnsureFrame(); OnUpdate(frame); }
    /// <summary>
    /// Advances state that depends on completed variable simulation.
    /// </summary>
    /// <param name="frame">
    /// The immutable variable-frame state.
    /// </param>
    public void LateUpdate(RuntimeFrame frame) { EnsureFrame(); OnLateUpdate(frame); }
    /// <summary>
    /// Prepares frame output after simulation has completed.
    /// </summary>
    /// <param name="frame">
    /// The immutable variable-frame state.
    /// </param>
    public void BeforeRender(RuntimeFrame frame) { EnsureFrame(); OnPrepareOutput(frame); }
    /// <summary>
    /// Produces frame output owned by this subsystem.
    /// </summary>
    /// <param name="frame">
    /// The immutable variable-frame state.
    /// </param>
    public void Render(RuntimeFrame frame) { EnsureFrame(); OnProduceOutput(frame); }
    /// <summary>
    /// Finalizes frame output even when rendering fails.
    /// </summary>
    /// <param name="frame">
    /// The immutable variable-frame state.
    /// </param>
    public void AfterRender(RuntimeFrame frame) { EnsureFrame(); OnCompleteOutput(frame); }
    /// <summary>
    /// Ends one frame and releases frame-scoped state.
    /// </summary>
    /// <param name="frame">
    /// The immutable variable-frame state.
    /// </param>
    public void EndFrame(RuntimeFrame frame)
    {
        if (!m_frameOpen)
            return;
        EnsureThread();
        List<Exception> failures = [];
        TryRelease(() => OnEndFrame(frame), failures);
        for (int index = m_frameScopes.Count - 1; index >= 0; index--)
            TryRelease(m_frameScopes[index].Dispose, failures);
        m_frameScopes.Clear();
        m_frameOpen = false;
        ThrowFailures(failures);
    }
    /// <summary>
    /// Releases attached session resources before dependencies detach.
    /// </summary>
    public void Detach() => Dispose();
    /// <summary>
    /// Releases this owner's registrations and resources exactly once.
    /// </summary>
    /// <exception cref="RetirementPendingException">
    /// Tracked work, the stop hook or a child resource is still draining. Retry without releasing dependencies.
    /// </exception>
    /// <exception cref="AggregateException">
    /// Retirement failed after every cleanup stage was attempted.
    /// </exception>
    public void Dispose()
    {
        if (m_disposed)
            return;
        EnsureThread();
        m_stopping = true;
        TryRelease(() => EndFrame(default), m_retirementFailures);
        TryRelease(m_lifetime.Cancel, m_retirementFailures);
        if (!m_lifetime.isQuiescent)
            throw new RetirementPendingException("Subsystem retirement is pending: tracked work must finish before domain resources can be released.");
        if (!m_stopCompleted)
        {
            TryRetire(OnStop);
            m_stopCompleted = true;
        }
        TryRetire(m_lifetime.Dispose);
        m_disposed = true;
        Exception[] failures = m_retirementFailures.ToArray();
        m_retirementFailures.Clear();
        if (failures.Length > 0)
            throw new AggregateException("Subsystem retirement failed after all cleanup stages were attempted.", failures);
    }

    /// <summary>
    /// Registers a binding that must close even when a frame hook fails.
    /// </summary>
    /// <param name="scope">
    /// The newly entered execution scope.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// No subsystem frame is open.
    /// </exception>
    protected void OwnFrameScope(IDisposable scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        EnsureFrame();
        m_frameScopes.Add(scope);
    }

    /// <summary>
    /// Acquires resources after dependencies have started.
    /// </summary>
    protected virtual void OnStart() { }
    /// <summary>
    /// Captures snapshots and binds service façades.
    /// </summary>
    /// <param name="frame">
    /// The variable frame timing.
    /// </param>
    protected virtual void OnBeginFrame(RuntimeFrame frame) { }
    /// <summary>
    /// Advances one deterministic fixed step.
    /// </summary>
    /// <param name="frame">
    /// The fixed-step timing.
    /// </param>
    protected virtual void OnFixedUpdate(RuntimeFixedFrame frame) { }
    /// <summary>
    /// Advances domain state on the variable clock.
    /// </summary>
    /// <param name="frame">
    /// The variable frame timing.
    /// </param>
    protected virtual void OnUpdate(RuntimeFrame frame) { }
    /// <summary>
    /// Updates state that depends on completed simulation.
    /// </summary>
    /// <param name="frame">
    /// The variable frame timing.
    /// </param>
    protected virtual void OnLateUpdate(RuntimeFrame frame) { }
    /// <summary>
    /// Opens resources required for this frame's output.
    /// </summary>
    /// <param name="frame">
    /// The variable frame timing.
    /// </param>
    protected virtual void OnPrepareOutput(RuntimeFrame frame) { }
    /// <summary>
    /// Collects output commands without executing managed code on a native realtime callback.
    /// </summary>
    /// <param name="frame">
    /// The variable frame timing.
    /// </param>
    protected virtual void OnProduceOutput(RuntimeFrame frame) { }
    /// <summary>
    /// Submits output and closes output-specific temporary resources.
    /// </summary>
    /// <param name="frame">
    /// The variable frame timing.
    /// </param>
    protected virtual void OnCompleteOutput(RuntimeFrame frame) { }
    /// <summary>
    /// Finalizes control-thread frame state before execution bindings are revoked.
    /// </summary>
    /// <param name="frame">
    /// The frame being closed.
    /// </param>
    protected virtual void OnEndFrame(RuntimeFrame frame) { }
    /// <summary>
    /// Releases domain resources, including allocations made before attachment.
    /// </summary>
    protected virtual void OnStop() { }

    private void EnsureStarted()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_stopping)
            throw new InvalidOperationException("The subsystem is retiring and cannot accept new work.");
        EnsureThread();
        if (!m_started)
            throw new InvalidOperationException("The subsystem has not been attached.");
    }
    private void EnsureFrame()
    {
        EnsureStarted();
        if (!m_frameOpen)
            throw new InvalidOperationException("No subsystem frame is active.");
    }
    private void EnsureThread()
    {
        if (m_ownerThread != 0 && m_ownerThread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Subsystem lifecycle operations require the owner thread.");
    }
    private static void TryRelease(Action release, List<Exception> failures)
    {
        try { release(); }
        catch (Exception exception) { failures.Add(exception); }
    }
    private void TryRetire(Action release)
    {
        try { release(); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception exception) { m_retirementFailures.Add(exception); }
    }
    private static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count > 0)
            throw new AggregateException("Subsystem retirement failed after all cleanup stages were attempted.", failures);
    }
}
