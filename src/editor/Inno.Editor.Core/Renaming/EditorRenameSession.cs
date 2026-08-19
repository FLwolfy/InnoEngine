using System;

namespace Inno.Editor.Core.Commands;

/// <summary>Stores the presentation-neutral state of one editor rename operation.</summary>
public sealed class EditorRenameSession
{
    private readonly Func<string, EditorValidationResult>? m_validate;
    private readonly Action<string> m_commit;
    private bool m_isCompleted;

    /// <summary>Creates a rename session.</summary>
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

    /// <summary>Validates the current buffer.</summary>
    public EditorValidationResult Validate()
        => m_validate?.Invoke(buffer) ?? EditorValidationResult.valid;

    /// <summary>Validates and commits the current buffer.</summary>
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

    /// <summary>Cancels the session without invoking its commit callback.</summary>
    public void Cancel() => m_isCompleted = true;
}
