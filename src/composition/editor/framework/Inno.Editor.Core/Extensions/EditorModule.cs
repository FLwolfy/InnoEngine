using System;
using System.Collections.Generic;

using Inno.Core.Execution;
using Inno.Scripting.Api;

namespace Inno.Editor.Core;

/// <summary>
/// Owns optional shared state and lifecycle for one editor feature.
/// Simple panels and actions do not need a module.
/// </summary>
public abstract class EditorModule : IDisposable
{
    private bool m_started;
    private bool m_disposed;
    private bool m_active;
    private EditorContext? m_context;
    private readonly List<Exception> m_retirementFailures = [];

    /// <summary>
    /// Gets whether modules ordered after this module must defer their updates for the current frame.
    /// </summary>
    /// <remarks>
    /// A module may return <see langword="true"/> while completing a project-wide bootstrap or atomic
    /// transition that later modules must not observe partially. Panels and modals remain drawable.
    /// </remarks>
    public virtual bool blocksFollowingUpdates => false;

    /// <summary>
    /// Starts the module after the containing extension generation becomes active.
    /// </summary>
    /// <param name="context">
    /// The shared editor context for the active runtime.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after the module has been disposed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the module is already started.
    /// </exception>
    [ScriptingApiIgnore]
    public void Start(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_started)
            throw new InvalidOperationException($"Editor module '{GetType().FullName}' is already started.");
        m_context = context;
        m_started = true;
        OnStart(context);
        m_active = true;
    }

    /// <summary>
    /// Updates the module once per editor frame before panels and modals are drawn.
    /// </summary>
    /// <param name="context">
    /// The shared editor context containing the current frame state.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after the module has been disposed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the module is not started.
    /// </exception>
    [ScriptingApiIgnore]
    public void Update(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!m_active)
            throw new InvalidOperationException($"Editor module '{GetType().FullName}' is not started.");
        OnUpdate(context);
    }

    /// <summary>
    /// Stops the module before the containing extension generation is released.
    /// </summary>
    /// <remarks>
    /// Also compensates a partially failed Start. Completed stops are idempotent; pending stops may be retried.
    /// </remarks>
    /// <param name="context">
    /// The shared editor context for the runtime being stopped.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after the module has been disposed.
    /// </exception>
    /// <exception cref="RetirementPendingException">
    /// Live work still depends on this module. Its owner must retain it and retry Stop before disposal.
    /// </exception>
    [ScriptingApiIgnore]
    public void Stop(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!m_started)
            return;
        m_active = false;
        try { OnStop(context); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch
        {
            m_started = false;
            m_context = null;
            throw;
        }
        m_started = false;
        m_context = null;
    }

    /// <summary>
    /// Runs after the module generation becomes active and before its first update.
    /// </summary>
    /// <param name="context">
    /// The shared editor context for the active runtime.
    /// </param>
    protected virtual void OnStart(EditorContext context)
    {
    }

    /// <summary>
    /// Runs once per editor frame before views are drawn.
    /// </summary>
    /// <param name="context">
    /// The shared editor context containing the current frame state.
    /// </param>
    protected virtual void OnUpdate(EditorContext context)
    {
    }

    /// <summary>
    /// Runs before the module generation is released and its disposable instances are destroyed.
    /// </summary>
    /// <remarks>
    /// This includes partial startup compensation. Pending retirement must throw RetirementPendingException
    /// and preserve unfinished ownership; completed steps must be idempotent across retries.
    /// </remarks>
    /// <param name="context">
    /// The shared editor context for the runtime being stopped.
    /// </param>
    protected virtual void OnStop(EditorContext context)
    {
    }

    /// <summary>
    /// Captures readable project state owned by this module.
    /// </summary>
    /// <remarks>
    /// Overriding this method opts the module into project-state IO. Modules that keep the base
    /// implementation are never registered with the persistence coordinator and therefore perform
    /// no state reads or writes.
    /// </remarks>
    /// <param name="state">
    /// The writable parameter that receives the complete readable state for this module.
    /// </param>
    protected virtual void Capture(EditorState state)
    {
    }

    /// <summary>
    /// Restores readable project state owned by this module.
    /// </summary>
    /// <remarks>
    /// This method is called only when <see cref="Capture"/> is overridden. It runs once after the
    /// module is started and before the module is allowed to capture replacement state.
    /// </remarks>
    /// <param name="state">
    /// The read-only state parameter for this module. Missing or incompatible values return the
    /// fallback supplied to <see cref="EditorState.Get{T}(string, T)"/>.
    /// </param>
    protected virtual void Restore(EditorState state)
    {
    }

    /// <summary>
    /// Releases resources owned by this module after it has stopped and left the active extension
    /// generation.
    /// </summary>
    protected virtual void OnDispose()
    {
    }

    void IDisposable.Dispose()
    {
        if (m_disposed)
            return;
        if (m_context is not null)
        {
            try { Stop(m_context); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception) { m_retirementFailures.Add(exception); }
        }
        try { OnDispose(); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception exception) { m_retirementFailures.Add(exception); }
        m_disposed = true;
        GC.SuppressFinalize(this);
        if (m_retirementFailures.Count > 0)
        {
            var failure = new AggregateException("Editor module retirement failed.", m_retirementFailures);
            m_retirementFailures.Clear();
            throw failure;
        }
    }
}
