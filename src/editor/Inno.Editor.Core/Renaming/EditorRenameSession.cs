using System;

namespace Inno.Editor.Core.Commands;

/// <summary>Stores the presentation-neutral state of one editor rename operation.</summary>
public sealed class EditorRenameSession
{
    private readonly Func<string, EditorValidationResult>? m_validate;
    private readonly Action<string> m_commit;
    private bool m_isCompleted;

    /// <summary>
    /// Creates a presentation-neutral rename session with optional validation and a required commit callback.
    /// </summary>
    /// <param name="target">The stable object represented by the rename UI.</param>
    /// <param name="value">The initial editable text.</param>
    /// <param name="validate">An optional callback that validates the current buffer without committing it.</param>
    /// <param name="commit">The callback invoked once after successful validation.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> or <paramref name="commit"/> is <see langword="null"/>.</exception>
    public EditorRenameSession(
        object target,
        string value,
        Func<string, EditorValidationResult>? validate,
        Action<string> commit)
    {
        this.target = target ?? throw new ArgumentNullException(nameof(target));
        buffer = value ?? string.Empty;
        m_validate = validate;
        m_commit = commit ?? throw new ArgumentNullException(nameof(commit));
    }

    /// <summary>Gets the object being renamed.</summary>
    public object target { get; }

    /// <summary>Gets or sets the editable text buffer.</summary>
    public string buffer { get; set; }

    /// <summary>Gets whether the session already finished.</summary>
    public bool isCompleted => m_isCompleted;

    /// <summary>
    /// Validates the current editable buffer without committing the rename.
    /// </summary>
    /// <returns>The validation result returned by the configured callback, or a valid result when no callback exists.</returns>
    public EditorValidationResult Validate()
        => m_validate?.Invoke(buffer) ?? EditorValidationResult.valid;

    /// <summary>
    /// Validates and commits the current editable buffer exactly once.
    /// </summary>
    /// <returns>The failed validation result, or the successful result after the commit callback completes.</returns>
    public EditorValidationResult Commit()
    {
        if (m_isCompleted)
            return EditorValidationResult.Invalid("The rename session is already complete.");
        EditorValidationResult validation = Validate();
        if (!validation.isValid)
            return validation;
        m_commit(buffer);
        m_isCompleted = true;
        return validation;
    }

    /// <summary>
    /// Cancels the session without invoking its validation or commit callbacks.
    /// </summary>
    public void Cancel() => m_isCompleted = true;
}
