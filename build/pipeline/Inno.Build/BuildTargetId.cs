using System;

namespace Inno.Build;

/// <summary>
/// Identifies one stable platform and architecture combination supported by the game build pipeline.
/// </summary>
public readonly record struct BuildTargetId
{
    /// <summary>
    /// Identifies a native Apple-silicon macOS Player.
    /// </summary>
    public static BuildTargetId macOSArm64 { get; } = new("macos-arm64");

    /// <summary>
    /// Identifies a native 64-bit Windows Player.
    /// </summary>
    public static BuildTargetId windowsX64 { get; } = new("windows-x64");

    /// <summary>
    /// Creates a stable target identity.
    /// </summary>
    /// <param name="value">
    /// A lowercase portable identifier composed of letters, numbers, and hyphens.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is not a portable target identity.
    /// </exception>
    public BuildTargetId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        foreach (char character in value)
        {
            if (!(character is >= 'a' and <= 'z' || character is >= '0' and <= '9' || character == '-'))
                throw new ArgumentException("Build target IDs must use lowercase portable characters.", nameof(value));
        }
        this.value = value;
    }

    /// <summary>
    /// Gets the portable target identity.
    /// </summary>
    public string value { get; }

    /// <summary>
    /// Formats the portable target identity.
    /// </summary>
    /// <returns>
    /// The exact stable target identity.
    /// </returns>
    public override string ToString()
        => value ?? string.Empty;
}
