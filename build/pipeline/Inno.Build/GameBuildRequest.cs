using System;
using System.IO;

namespace Inno.Build;

/// <summary>
/// Requests one game build from the current validated authoring generation.
/// </summary>
public sealed class GameBuildRequest
{
    /// <summary>
    /// Gets or initializes the reusable product profile.
    /// </summary>
    public required BuildProfile profile { get; init; }

    /// <summary>
    /// Gets or initializes the parent directory that receives the atomically committed platform output.
    /// </summary>
    public required string outputDirectory { get; init; }

    /// <summary>
    /// Validates destination and product inputs without mutating output.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Thrown when the request is incomplete.
    /// </exception>
    public void Validate()
    {
        if (profile is null)
            throw new InvalidDataException("A game build profile is required.");
        profile.Validate();
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidDataException("A game output directory is required.");
    }
}
