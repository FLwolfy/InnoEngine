using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Inno.Core.Serialization;

namespace Inno.Build;

/// <summary>
/// Defines reusable product and startup settings for deterministic game builds.
/// </summary>
[GenerateSerializationConverter]
public sealed class BuildProfile : ISerializable
{
    private static readonly HashSet<string> S_WINDOWS_RESERVED_NAMES = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Gets or sets the stable lowercase identity used for persistent application data.
    /// </summary>
    [SerializableProperty]
    public string applicationId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the player-facing product and output name.
    /// </summary>
    [SerializableProperty]
    public string productName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mount-qualified startup scene path.
    /// </summary>
    [SerializableProperty]
    public string startupScene { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the platform target.
    /// </summary>
    public BuildTargetId target
    {
        get => new(m_targetId);
        set => m_targetId = value.value;
    }

    /// <summary>
    /// Gets or sets the initial logical window width.
    /// </summary>
    [SerializableProperty]
    public int windowWidth { get; set; } = 1280;

    /// <summary>
    /// Gets or sets the initial logical window height.
    /// </summary>
    [SerializableProperty]
    public int windowHeight { get; set; } = 720;

    [SerializableProperty]
    internal string m_targetId = BuildTargetId.macOSArm64.value;

    /// <summary>
    /// Validates product identity, startup content, target, and window dimensions.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Thrown when the profile cannot produce a portable Player deployment.
    /// </exception>
    public void Validate()
    {
        if (!IsPortableIdentifier(applicationId))
            throw new InvalidDataException("Application ID must be a stable lowercase portable identifier.");
        if (!IsPortableProductName(productName))
            throw new InvalidDataException("Product name must be a portable file name.");
        if (string.IsNullOrWhiteSpace(startupScene))
            throw new InvalidDataException("A startup scene is required.");
        BuildTargetId validatedTarget;
        try
        {
            validatedTarget = new BuildTargetId(m_targetId);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("A game build target is required and must use a portable target ID.", exception);
        }
        if (validatedTarget != BuildTargetId.macOSArm64 && validatedTarget != BuildTargetId.windowsX64)
            throw new InvalidDataException($"Build target '{validatedTarget}' is not supported.");
        if (windowWidth <= 0 || windowHeight <= 0)
            throw new InvalidDataException("Window dimensions must be positive.");
    }

    private static bool IsPortableIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return value.All(static character =>
            character is >= 'a' and <= 'z'
            || character is >= '0' and <= '9'
            || character is '.' or '_' or '-');
    }

    private static bool IsPortableProductName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value is "." or ".."
            || value.EndsWith(' ')
            || value.EndsWith('.'))
        {
            return false;
        }
        if (value.Any(static character => character < ' ' || "<>:\"/\\|?*".Contains(character)))
            return false;
        string stem = value.Split('.', 2)[0];
        return !S_WINDOWS_RESERVED_NAMES.Contains(stem);
    }
}
