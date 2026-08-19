using System;

namespace Inno.Editor.Core.Commands;

/// <summary>Defines one discoverable contextual editor action.</summary>
public abstract class EditorAction
{
    /// <summary>Gets the required target type, or <see langword="null"/> for a targetless action.</summary>
    public virtual Type? targetType => null;

    /// <summary>Evaluates the action for the supplied context.</summary>
    public virtual EditorActionState Query(EditorActionContext context)
        => EditorActionState.enabled;

    /// <summary>Executes the action for the supplied context.</summary>
    public abstract void Execute(EditorActionContext context);
}

/// <summary>Defines a discoverable action for one target type.</summary>
public abstract class EditorAction<TTarget> : EditorAction
    where TTarget : class
{
    /// <inheritdoc />
    public sealed override Type targetType => typeof(TTarget);

    /// <inheritdoc />
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

    /// <summary>Evaluates the action for a typed target.</summary>
    protected virtual EditorActionState Query(EditorActionContext<TTarget> context)
        => EditorActionState.enabled;

    /// <summary>Executes the action for a typed target.</summary>
    protected abstract void Execute(EditorActionContext<TTarget> context);
}
