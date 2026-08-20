using System;

namespace Inno.Editor.Core.Commands;

/// <summary>
/// Defines one automatically discovered editor operation that can be queried and executed in a contextual surface.
/// </summary>
public abstract class EditorAction
{
    /// <summary>Gets the required target type, or <see langword="null"/> for a targetless action.</summary>
    public virtual Type? targetType => null;

    /// <summary>
    /// Evaluates the presentation and availability of the action for the supplied context.
    /// </summary>
    /// <param name="context">The editor, surface, target, and optional argument for the query.</param>
    /// <returns>The visibility, enabled state, checked state, and optional display name of the action.</returns>
    public virtual EditorActionState Query(EditorActionContext context)
        => EditorActionState.enabled;

    /// <summary>
    /// Executes the action for the supplied context after a successful availability query.
    /// </summary>
    /// <param name="context">The editor, surface, target, and optional argument for the operation.</param>
    public abstract void Execute(EditorActionContext context);
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
    /// <param name="context">The untyped action context supplied by the runtime router.</param>
    /// <returns>The typed action state, or a hidden state when the target is incompatible.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the target is not assignable to <typeparamref name="TTarget"/>.</exception>
    public sealed override EditorActionState Query(EditorActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.target is TTarget target
            ? Query(new EditorActionContext<TTarget>(
                context.editor,
                context.surface,
                target,
                context.argument))
            : EditorActionState.hidden;
    }

    /// <inheritdoc />
    /// <param name="context">The untyped action context supplied by the runtime router.</param>
    public sealed override void Execute(EditorActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.target is not TTarget target)
            throw new InvalidOperationException(
                $"Editor action '{GetType().FullName}' requires target '{typeof(TTarget).FullName}'.");
        Execute(new EditorActionContext<TTarget>(
            context.editor,
            context.surface,
            target,
            context.argument));
    }

    /// <summary>
    /// Evaluates the presentation and availability of the action for a strongly typed target.
    /// </summary>
    /// <param name="context">The action context containing a target of type <typeparamref name="TTarget"/>.</param>
    /// <returns>The visibility, enabled state, checked state, and optional display name of the action.</returns>
    protected virtual EditorActionState Query(EditorActionContext<TTarget> context)
        => EditorActionState.enabled;

    /// <summary>
    /// Executes the action for a strongly typed target.
    /// </summary>
    /// <param name="context">The action context containing a target of type <typeparamref name="TTarget"/>.</param>
    protected abstract void Execute(EditorActionContext<TTarget> context);
}
