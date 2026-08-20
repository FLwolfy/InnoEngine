using System;

namespace Inno.Editor.Interactions;

/// <summary>
/// Defines one automatically discovered editor operation and owns its complete multi-frame lifecycle.
/// </summary>
public abstract class EditorAction : IDisposable
{
    private object? m_activeTarget;
    private bool m_isActive;
    private bool m_isDisposed;

    /// <summary>Gets whether the action currently owns an active multi-frame operation.</summary>
    public bool isActive => m_isActive;

    /// <summary>Gets the required target type, or <see langword="null"/> for a targetless action.</summary>
    public virtual Type? targetType => null;

    internal EditorActionState QueryInternal(EditorActionContext context)
    {
        ObjectDisposedException.ThrowIf(m_isDisposed, this);
        return Query(context);
    }

    internal void ExecuteInternal(EditorActionContext context)
    {
        ObjectDisposedException.ThrowIf(m_isDisposed, this);
        Execute(context);
    }

    internal bool PresentInternal(EditorActionContext context)
    {
        ObjectDisposedException.ThrowIf(m_isDisposed, this);
        return m_isActive && Equals(m_activeTarget, context.target) && Present(context);
    }

    internal void CancelInternal() => Cancel();

    internal bool IsActiveFor(object? target)
        => m_isActive && Equals(m_activeTarget, target);

    /// <summary>
    /// Activates this action for a target and cancels any operation it previously owned.
    /// </summary>
    /// <param name="context">The contextual request that starts the operation.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is <see langword="null"/>.</exception>
    protected void Activate(EditorActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Cancel();
        m_activeTarget = context.target;
        m_isActive = true;
    }

    /// <summary>Completes the current operation and returns the action to its idle state.</summary>
    protected void Complete()
    {
        if (!m_isActive)
            return;
        m_activeTarget = null;
        m_isActive = false;
        OnCompleted();
    }

    /// <summary>Cancels the current operation and returns the action to its idle state.</summary>
    protected void Cancel()
    {
        if (!m_isActive)
            return;
        m_activeTarget = null;
        m_isActive = false;
        OnCancelled();
    }

    /// <summary>Evaluates the action for the supplied context.</summary>
    /// <param name="context">The editor, area, target, and optional argument for the query.</param>
    /// <returns>The current presentation and availability state.</returns>
    protected virtual EditorActionState Query(EditorActionContext context)
        => EditorActionState.enabled;

    /// <summary>Executes the action for the supplied context.</summary>
    /// <param name="context">The editor, area, target, and optional argument for the operation.</param>
    protected abstract void Execute(EditorActionContext context);

    /// <summary>Presents an active action at the current target location.</summary>
    /// <param name="context">The current presentation area and target.</param>
    /// <returns><see langword="true"/> when the action replaced the target's normal content; otherwise, <see langword="false"/>.</returns>
    protected virtual bool Present(EditorActionContext context) => false;

    /// <summary>Runs after an active operation completes successfully.</summary>
    protected virtual void OnCompleted()
    {
    }

    /// <summary>Runs after an active operation is cancelled.</summary>
    protected virtual void OnCancelled()
    {
    }

    /// <summary>Cancels active work and releases this action instance.</summary>
    public void Dispose()
    {
        if (m_isDisposed)
            return;
        Cancel();
        m_isDisposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Defines a discoverable action whose target must be assignable to a specific reference type.
/// </summary>
/// <typeparam name="TTarget">The target type accepted by the action.</typeparam>
public abstract class EditorAction<TTarget> : EditorAction
    where TTarget : class
{
    /// <inheritdoc />
    public sealed override Type targetType => typeof(TTarget);

    /// <inheritdoc />
    protected sealed override EditorActionState Query(EditorActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.target is TTarget target
            ? Query(new EditorActionContext<TTarget>(context, target))
            : EditorActionState.hidden;
    }

    /// <inheritdoc />
    protected sealed override void Execute(EditorActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.target is not TTarget target)
        {
            throw new InvalidOperationException(
                $"Editor action '{GetType().FullName}' requires target '{typeof(TTarget).FullName}'.");
        }
        Execute(new EditorActionContext<TTarget>(context, target));
    }

    /// <inheritdoc />
    protected sealed override bool Present(EditorActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.target is TTarget target &&
               Present(new EditorActionContext<TTarget>(context, target));
    }

    /// <summary>Evaluates the action for a strongly typed target.</summary>
    /// <param name="context">The contextual request containing the typed target.</param>
    /// <returns>The current presentation and availability state.</returns>
    protected virtual EditorActionState Query(EditorActionContext<TTarget> context)
        => EditorActionState.enabled;

    /// <summary>Executes the action for a strongly typed target.</summary>
    /// <param name="context">The contextual request containing the typed target.</param>
    protected abstract void Execute(EditorActionContext<TTarget> context);

    /// <summary>Presents an active action for a strongly typed target.</summary>
    /// <param name="context">The current presentation request containing the typed target.</param>
    /// <returns><see langword="true"/> when content was presented; otherwise, <see langword="false"/>.</returns>
    protected virtual bool Present(EditorActionContext<TTarget> context) => false;
}
