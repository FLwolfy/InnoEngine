using System;

namespace Inno.Build;

/// <summary>
/// Reports monotonic progress for one asynchronous build operation.
/// </summary>
public readonly record struct BuildProgress
{
    /// <summary>
    /// Creates a validated progress update.
    /// </summary>
    /// <param name="stage">
    /// The stable build stage name.
    /// </param>
    /// <param name="fraction">
    /// The completed fraction in the inclusive range from zero to one.
    /// </param>
    /// <param name="message">
    /// A concise human-readable status message.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when stage or message is empty.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when fraction is outside the supported range.
    /// </exception>
    public BuildProgress(string stage, double fraction, string message)
        : this()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (fraction is < 0d or > 1d || double.IsNaN(fraction))
            throw new ArgumentOutOfRangeException(nameof(fraction));
        this.stage = stage;
        this.fraction = fraction;
        this.message = message;
    }

    /// <summary>
    /// Gets the stable build stage name.
    /// </summary>
    public string stage { get; }

    /// <summary>
    /// Gets the completed fraction in the inclusive range from zero to one.
    /// </summary>
    public double fraction { get; }

    /// <summary>
    /// Gets a concise human-readable status message.
    /// </summary>
    public string message { get; }
}
