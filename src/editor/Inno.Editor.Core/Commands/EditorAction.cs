using System;

namespace Inno.Editor.Core.Commands;

/// <summary>
/// Defines one automatically discovered editor operation that can be queried and executed in a contextual surface.
/// </summary>
public abstract class EditorAction
{
    private EditorActionInteraction? m_interaction;

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

    /// <summary>
    /// Tries to resolve the active cross-frame interaction for a compatible target and state type.
    /// </summary>
    /// <typeparam name="TState">The state type expected by the interaction presenter.</typeparam>
    /// <param name="target">The optional target whose interaction should be resolved.</param>
    /// <param name="interaction">The active typed interaction when resolution succeeds.</param>
    /// <returns><see langword="true"/> when this action owns a non-completed interaction for the supplied target; otherwise, <see langword="false"/>.</returns>
    public bool TryGetInteraction<TState>(
        object? target,
        out EditorActionInteraction<TState>? interaction)
    {
        interaction = m_interaction as EditorActionInteraction<TState>;
        if (interaction is null ||
            interaction.isCompleted ||
            !Equals(interaction.target, target))
        {
            interaction = null;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Begins a type-safe interaction owned by this action and cancels any interaction it previously owned.
    /// </summary>
    /// <typeparam name="TState">The neutral state type presented across UI frames.</typeparam>
    /// <param name="context">The action context that starts the interaction.</param>
    /// <param name="state">The initial interaction state.</param>
    /// <param name="complete">The callback that commits validated state.</param>
    /// <param name="validate">An optional callback that validates state without committing it.</param>
    /// <param name="cancel">An optional callback invoked when the interaction is cancelled.</param>
    /// <returns>The newly active interaction.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> or <paramref name="complete"/> is <see langword="null"/>.</exception>
    protected EditorActionInteraction<TState> BeginInteraction<TState>(
        EditorActionContext context,
        TState state,
        Action<TState> complete,
        Func<TState, EditorValidationResult>? validate = null,
        Action<TState>? cancel = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(complete);
        m_interaction?.Cancel();
        var interaction = new EditorActionInteraction<TState>(
            context.surface,
            context.target,
            state,
            validate,
            complete,
            cancel);
        m_interaction = interaction;
        return interaction;
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
