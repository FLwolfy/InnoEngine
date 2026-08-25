using System;

namespace Inno.Editor.Interactions;

/// <summary>
/// Describes the outcome of applying, undoing, or redoing an editor history operation.
/// </summary>
public readonly record struct EditorHistoryResult
{
    private EditorHistoryResult(bool succeeded, bool statePreserved, string message)
    {
        this.succeeded = succeeded;
        this.statePreserved = statePreserved;
        this.message = message;
    }

    /// <summary>
    /// Gets whether the requested history transition completed successfully.
    /// </summary>
    public bool succeeded { get; }

    /// <summary>
    /// Gets whether a failed transition restored the domain state that existed before the attempt.
    /// </summary>
    /// <remarks>
    /// Successful results always preserve a valid state. A failed result with this value set to
    /// <see langword="false"/> faults the owning history because the transition can no longer be retried safely.
    /// </remarks>
    public bool statePreserved { get; }

    /// <summary>
    /// Gets the diagnostic message associated with a failed transition, or an empty string after success.
    /// </summary>
    public string message { get; }

    /// <summary>
    /// Creates a successful history result.
    /// </summary>
    /// <returns>A result representing a completed transition.</returns>
    public static EditorHistoryResult Success() => new(true, true, string.Empty);

    /// <summary>
    /// Creates a failed history result without changing the owning history stack.
    /// </summary>
    /// <param name="message">The non-empty diagnostic explaining why the transition could not complete.</param>
    /// <returns>A result representing a rejected or failed transition.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="message"/> is empty.</exception>
    public static EditorHistoryResult Failure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new EditorHistoryResult(false, true, message);
    }

    internal static EditorHistoryResult StateIntegrityLost(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new EditorHistoryResult(false, false, message);
    }
}
