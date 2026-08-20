using System;

namespace Inno.Editor.Core.Commands;

/// <summary>
/// Represents presentation-neutral state owned by an editor action across multiple UI frames.
/// </summary>
public abstract class EditorActionInteraction
{
    private bool m_isCompleted;

    /// <summary>
    /// Creates action interaction state for one surface and target.
    /// </summary>
    /// <param name="surface">The interaction surface that started the action.</param>
    /// <param name="target">The optional target associated with the action.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="surface"/> is <see langword="null"/>.</exception>
    protected EditorActionInteraction(Type surface, object? target)
    {
        this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
        this.target = target;
    }

    /// <summary>
    /// Gets the interaction surface that started the action.
    /// </summary>
    public Type surface { get; }

    /// <summary>
    /// Gets the optional target associated with the action.
    /// </summary>
    public object? target { get; }

    /// <summary>
    /// Gets whether the interaction has been completed or cancelled.
    /// </summary>
    public bool isCompleted => m_isCompleted;

    /// <summary>
    /// Cancels the interaction without completing its action.
    /// </summary>
    public void Cancel()
    {
        if (m_isCompleted)
            return;
        m_isCompleted = true;
        OnCancelled();
    }

    /// <summary>
    /// Executes type-specific cancellation behavior exactly once.
    /// </summary>
    protected virtual void OnCancelled()
    {
    }

    /// <summary>
    /// Marks the interaction as successfully completed.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the interaction has already completed.</exception>
    protected void MarkCompleted()
    {
        if (m_isCompleted)
            throw new InvalidOperationException("The editor action interaction is already complete.");
        m_isCompleted = true;
    }
}

/// <summary>
/// Stores mutable, type-safe state for an editor action that spans multiple UI frames.
/// </summary>
/// <typeparam name="TState">The neutral state edited or observed while the action remains active.</typeparam>
public sealed class EditorActionInteraction<TState> : EditorActionInteraction
{
    private readonly Func<TState, EditorValidationResult>? m_validate;
    private readonly Action<TState> m_complete;
    private readonly Action<TState>? m_cancel;

    internal EditorActionInteraction(
        Type surface,
        object? target,
        TState state,
        Func<TState, EditorValidationResult>? validate,
        Action<TState> complete,
        Action<TState>? cancel)
        : base(surface, target)
    {
        this.state = state;
        m_validate = validate;
        m_complete = complete ?? throw new ArgumentNullException(nameof(complete));
        m_cancel = cancel;
    }

    /// <summary>
    /// Gets or sets the mutable state presented by the active action.
    /// </summary>
    public TState state { get; set; }

    /// <summary>
    /// Validates the current state without completing the action.
    /// </summary>
    /// <returns>The configured validation result, or a valid result when no validator was supplied.</returns>
    public EditorValidationResult Validate()
        => m_validate?.Invoke(state) ?? EditorValidationResult.valid;

    /// <summary>
    /// Validates the current state and completes the action exactly once.
    /// </summary>
    /// <returns>The validation result. A failed result leaves the interaction active for further editing.</returns>
    public EditorValidationResult Complete()
    {
        if (isCompleted)
            return EditorValidationResult.Invalid("The editor action interaction is already complete.");
        EditorValidationResult validation = Validate();
        if (!validation.isValid)
            return validation;
        m_complete(state);
        MarkCompleted();
        return validation;
    }

    /// <inheritdoc />
    protected override void OnCancelled() => m_cancel?.Invoke(state);
}
