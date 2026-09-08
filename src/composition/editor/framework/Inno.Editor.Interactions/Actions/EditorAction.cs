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

    /// <summary>
    /// Gets whether the action currently owns an active multi-frame operation.
    /// </summary>
    public bool isActive => m_isActive;

    /// <summary>
    /// Gets the required target type, or <see langword="null"/> for a targetless action.
    /// </summary>
    public virtual Type? targetType => null;

    internal virtual Type? argumentType => null;

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

    internal void LosePresentationInternal()
    {
        if (!m_isActive)
            return;
        try
        {
            OnPresentationLost();
        }
        finally
        {
            if (m_isActive)
                Cancel();
        }
    }

    internal bool IsActiveFor(object? target)
        => m_isActive && Equals(m_activeTarget, target);

    /// <summary>
    /// Activates this action for a target and cancels any operation it previously owned.
    /// </summary>
    /// <param name="context">
    /// The contextual request that starts the operation.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    protected void Activate(EditorActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Cancel();
        m_activeTarget = context.target;
        m_isActive = true;
    }

    /// <summary>
    /// Completes the current operation and returns the action to its idle state.
    /// </summary>
    protected void Complete()
    {
        if (!m_isActive)
            return;
        m_activeTarget = null;
        m_isActive = false;
        OnCompleted();
    }

    /// <summary>
    /// Cancels the current operation and returns the action to its idle state.
    /// </summary>
    protected void Cancel()
    {
        if (!m_isActive)
            return;
        m_activeTarget = null;
        m_isActive = false;
        OnCancelled();
    }

    /// <summary>
    /// Evaluates the action for the supplied context.
    /// </summary>
    /// <param name="context">
    /// The editor, area, target, and optional argument for the query.
    /// </param>
    /// <returns>
    /// The current presentation and availability state.
    /// </returns>
    protected virtual EditorActionState Query(EditorActionContext context)
        => EditorActionState.enabled;

    /// <summary>
    /// Executes the action for the supplied context.
    /// </summary>
    /// <param name="context">
    /// The editor, area, target, and optional argument for the operation.
    /// </param>
    protected abstract void Execute(EditorActionContext context);

    /// <summary>
    /// Presents an active action at the current target location.
    /// </summary>
    /// <param name="context">
    /// The current presentation area and target.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the action replaced the target's normal content; otherwise, <see langword="false"/>.
    /// </returns>
    protected virtual bool Present(EditorActionContext context) => false;

    /// <summary>
    /// Runs after an active operation completes successfully.
    /// </summary>
    protected virtual void OnCompleted()
    {
    }

    /// <summary>
    /// Runs after an active operation is cancelled.
    /// </summary>
    protected virtual void OnCancelled()
    {
    }

    /// <summary>
    /// Runs when this action's active target loses editor presentation focus.
    /// </summary>
    /// <remarks>
    /// The default behavior cancels the active operation. An action that owns an editable value may override this
    /// method to validate and commit that value. The operation is cancelled automatically when an override returns
    /// without calling <see cref="Complete"/> or <see cref="Cancel"/>.
    /// </remarks>
    protected virtual void OnPresentationLost() => Cancel();

    /// <summary>
    /// Cancels active work and releases this action instance.
    /// </summary>
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
/// Defines a target action that requires one strongly typed command argument.
/// </summary>
/// <typeparam name="TTarget">
/// The target type accepted by the action.
/// </typeparam>
/// <typeparam name="TArgument">
/// The command argument type accepted by the action.
/// </typeparam>
public abstract class EditorAction<TTarget, TArgument> : EditorAction
    where TTarget : class
{
    /// <summary>
    /// Gets the concrete type handled by this extension implementation.
    /// </summary>
    public sealed override Type targetType => typeof(TTarget);

    internal sealed override Type argumentType => typeof(TArgument);

    /// <summary>
    /// Evaluates the operation's current availability and presentation state.
    /// </summary>
    /// <returns>
    /// The validated editor action state that represents the completed operation.
    /// </returns>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected sealed override EditorActionState Query(EditorActionContext context)
        => TryCreate(context, out EditorActionContext<TTarget, TArgument> typed)
            ? Query(typed)
            : EditorActionState.hidden;

    /// <summary>
    /// Applies the editor action to the supplied interaction context.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected sealed override void Execute(EditorActionContext context)
    {
        if (!TryCreate(context, out EditorActionContext<TTarget, TArgument> typed))
        {
            throw new InvalidOperationException(
                $"Editor action '{GetType().FullName}' requires target '{typeof(TTarget).FullName}' " +
                $"and argument '{typeof(TArgument).FullName}'.");
        }
        Execute(typed);
    }

    /// <summary>
    /// Presents this action through the current editor interaction surface.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the operation succeeds or its condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected sealed override bool Present(EditorActionContext context)
        => TryCreate(context, out EditorActionContext<TTarget, TArgument> typed) && Present(typed);

    /// <summary>
    /// Evaluates the action for a typed target and argument.
    /// </summary>
    /// <param name="context">
    /// The typed contextual request.
    /// </param>
    /// <returns>
    /// The current presentation and availability state.
    /// </returns>
    protected virtual EditorActionState Query(EditorActionContext<TTarget, TArgument> context)
        => EditorActionState.enabled;

    /// <summary>
    /// Executes the action for a typed target and argument.
    /// </summary>
    /// <param name="context">
    /// The typed contextual request.
    /// </param>
    protected abstract void Execute(EditorActionContext<TTarget, TArgument> context);

    /// <summary>
    /// Presents an active action for a typed target and argument.
    /// </summary>
    /// <param name="context">
    /// The typed presentation request.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when content was presented.
    /// </returns>
    protected virtual bool Present(EditorActionContext<TTarget, TArgument> context) => false;

    private static bool TryCreate(
        EditorActionContext context,
        out EditorActionContext<TTarget, TArgument> typed)
    {
        if (context.target is TTarget target && context.argument is TArgument argument)
        {
            typed = new EditorActionContext<TTarget, TArgument>(context, target, argument);
            return true;
        }
        typed = null!;
        return false;
    }
}

/// <summary>
/// Defines a targetless editor action that requires one strongly typed command argument.
/// </summary>
/// <typeparam name="TArgument">
/// The command argument type accepted by the action.
/// </typeparam>
public abstract class EditorArgumentAction<TArgument> : EditorAction
{
    internal sealed override Type argumentType => typeof(TArgument);

    /// <summary>
    /// Evaluates the operation's current availability and presentation state.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    /// <returns>
    /// The validated editor action state that represents the completed operation.
    /// </returns>
    protected sealed override EditorActionState Query(EditorActionContext context)
        => context.target is null && context.argument is TArgument argument
            ? Query(new EditorActionArgumentContext<TArgument>(context, argument))
            : EditorActionState.hidden;

    /// <summary>
    /// Applies the editor action to the supplied interaction context.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected sealed override void Execute(EditorActionContext context)
    {
        if (context.target is not null || context.argument is not TArgument argument)
        {
            throw new InvalidOperationException(
                $"Editor action '{GetType().FullName}' requires argument '{typeof(TArgument).FullName}'.");
        }
        Execute(new EditorActionArgumentContext<TArgument>(context, argument));
    }

    /// <summary>
    /// Evaluates the action for a typed argument.
    /// </summary>
    /// <param name="context">
    /// The typed contextual request.
    /// </param>
    /// <returns>
    /// The current presentation and availability state.
    /// </returns>
    protected virtual EditorActionState Query(EditorActionArgumentContext<TArgument> context)
        => EditorActionState.enabled;

    /// <summary>
    /// Executes the action for a typed argument.
    /// </summary>
    /// <param name="context">
    /// The typed contextual request.
    /// </param>
    protected abstract void Execute(EditorActionArgumentContext<TArgument> context);
}

/// <summary>
/// Defines a target action with a typed presentation-only argument.
/// </summary>
/// <typeparam name="TTarget">
/// The target type accepted by the action.
/// </typeparam>
/// <typeparam name="TPresentation">
/// The presentation data type.
/// </typeparam>
public abstract class EditorPresentationAction<TTarget, TPresentation> : EditorAction<TTarget>
    where TTarget : class
{
    /// <summary>
    /// Presents this action through the current editor interaction surface.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the operation succeeds or its condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    protected sealed override bool Present(EditorActionContext<TTarget> context)
        => context.argument is TPresentation presentation && Present(
            new EditorActionContext<TTarget, TPresentation>(context, context.target, presentation));

    /// <summary>
    /// Presents active content using strongly typed presentation data.
    /// </summary>
    /// <param name="context">
    /// The typed presentation request.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when content was presented.
    /// </returns>
    protected abstract bool Present(EditorActionContext<TTarget, TPresentation> context);
}

/// <summary>
/// Defines a discoverable action whose target must be assignable to a specific reference type.
/// </summary>
/// <typeparam name="TTarget">
/// The target type accepted by the action.
/// </typeparam>
public abstract class EditorAction<TTarget> : EditorAction
    where TTarget : class
{
    /// <summary>
    /// Gets the concrete type handled by this extension implementation.
    /// </summary>
    public sealed override Type targetType => typeof(TTarget);

    /// <summary>
    /// Evaluates the operation's current availability and presentation state.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    /// <returns>
    /// The validated editor action state that represents the completed operation.
    /// </returns>
    protected sealed override EditorActionState Query(EditorActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.target is TTarget target
            ? Query(new EditorActionContext<TTarget>(context, target))
            : EditorActionState.hidden;
    }

    /// <summary>
    /// Applies the editor action to the supplied interaction context.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
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

    /// <summary>
    /// Presents this action through the current editor interaction surface.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the operation succeeds or its condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    protected sealed override bool Present(EditorActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.target is TTarget target &&
               Present(new EditorActionContext<TTarget>(context, target));
    }

    /// <summary>
    /// Evaluates the action for a strongly typed target.
    /// </summary>
    /// <param name="context">
    /// The contextual request containing the typed target.
    /// </param>
    /// <returns>
    /// The current presentation and availability state.
    /// </returns>
    protected virtual EditorActionState Query(EditorActionContext<TTarget> context)
        => EditorActionState.enabled;

    /// <summary>
    /// Executes the action for a strongly typed target.
    /// </summary>
    /// <param name="context">
    /// The contextual request containing the typed target.
    /// </param>
    protected abstract void Execute(EditorActionContext<TTarget> context);

    /// <summary>
    /// Presents an active action for a strongly typed target.
    /// </summary>
    /// <param name="context">
    /// The current presentation request containing the typed target.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when content was presented; otherwise, <see langword="false"/>.
    /// </returns>
    protected virtual bool Present(EditorActionContext<TTarget> context) => false;
}
