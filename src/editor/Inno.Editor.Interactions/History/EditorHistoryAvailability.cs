using System;

namespace Inno.Editor.Interactions;

/// <summary>
/// Describes whether a history change can currently move in one direction.
/// </summary>
public readonly record struct EditorHistoryAvailability
{
    private EditorHistoryAvailability(bool isAvailable, string message)
    {
        this.isAvailable = isAvailable;
        this.message = message;
    }

    /// <summary>
    /// Gets whether the requested transition is currently available.
    /// </summary>
    public bool isAvailable { get; }

    /// <summary>
    /// Gets the diagnostic explaining why the transition is unavailable, or an empty string when available.
    /// </summary>
    public string message { get; }

    /// <summary>
    /// Creates an available transition result.
    /// </summary>
    /// <returns>An availability value that permits the transition.</returns>
    public static EditorHistoryAvailability Available() => new(true, string.Empty);

    /// <summary>
    /// Creates an unavailable transition result.
    /// </summary>
    /// <param name="message">The non-empty diagnostic explaining why the transition is unavailable.</param>
    /// <returns>An availability value that rejects the transition.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="message"/> is empty.</exception>
    public static EditorHistoryAvailability Unavailable(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new EditorHistoryAvailability(false, message);
    }
}
