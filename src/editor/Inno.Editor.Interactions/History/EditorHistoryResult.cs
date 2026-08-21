using System;

namespace Inno.Editor.Interactions;

/// <summary>
/// Describes the outcome of applying, undoing, or redoing an editor history operation.
/// </summary>
public readonly record struct EditorHistoryResult
{
    private EditorHistoryResult(bool succeeded, string message)
    {
        this.succeeded = succeeded;
        this.message = message;
    }

    /// <summary>
    /// Gets whether the requested history transition completed successfully.
    /// </summary>
    public bool succeeded { get; }

    /// <summary>
    /// Gets the diagnostic message associated with a failed transition, or an empty string after success.
    /// </summary>
    public string message { get; }

    /// <summary>
    /// Creates a successful history result.
    /// </summary>
    /// <returns>A result representing a completed transition.</returns>
    public static EditorHistoryResult Success() => new(true, string.Empty);

    /// <summary>
    /// Creates a failed history result without changing the owning history stack.
    /// </summary>
    /// <param name="message">The non-empty diagnostic explaining why the transition could not complete.</param>
    /// <returns>A result representing a rejected or failed transition.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="message"/> is empty.</exception>
    public static EditorHistoryResult Failure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new EditorHistoryResult(false, message);
    }
}
