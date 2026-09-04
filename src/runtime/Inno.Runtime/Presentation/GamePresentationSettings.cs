using System;

using Inno.Core.Serialization;
using Inno.Core.Settings;
using Inno.Extensibility.Types;

namespace Inno.Runtime;

/// <summary>
/// Defines the project-wide game presentation area shared by Editor previews and deployed Players.
/// </summary>
[GenerateSerializationConverter]
[StableTypeId("4068d172-87ac-41b3-ae64-247d25889336")]
[ProjectSettingDefinition("inno.runtime.game-presentation")]
public sealed class GamePresentationSettings : ISerializable
{
    /// <summary>
    /// Gets the stable project-setting identity for game presentation.
    /// </summary>
    public static ProjectSettingId settingId => new("inno.runtime.game-presentation");

    /// <summary>
    /// Gets or sets whether the complete reference frame is fitted inside the available surface.
    /// </summary>
    [SerializableProperty]
    public bool preserveAspectRatio { get; set; } = true;

    /// <summary>
    /// Gets or sets the positive reference-frame width used to derive the presentation aspect ratio.
    /// </summary>
    [SerializableProperty]
    public int referenceWidth { get; set; } = 1280;

    /// <summary>
    /// Gets or sets the positive reference-frame height used to derive the presentation aspect ratio.
    /// </summary>
    [SerializableProperty]
    public int referenceHeight { get; set; } = 720;

    /// <summary>
    /// Calculates the centered content region for an available presentation surface.
    /// </summary>
    /// <param name="availableWidth">
    /// Positive available surface width in pixels or logical preview units.
    /// </param>
    /// <param name="availableHeight">
    /// Positive available surface height in pixels or logical preview units.
    /// </param>
    /// <returns>
    /// A positive region that fills the surface or fits the configured reference aspect ratio.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when an available or reference dimension is not positive.
    /// </exception>
    public GamePresentationViewport CalculateViewport(int availableWidth, int availableHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(availableWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(availableHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(referenceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(referenceHeight);
        if (!preserveAspectRatio)
            return new GamePresentationViewport(0, 0, availableWidth, availableHeight);

        double targetAspect = referenceWidth / (double)referenceHeight;
        int width = availableWidth;
        int height = Math.Max(1, (int)Math.Floor(width / targetAspect));
        if (height > availableHeight)
        {
            height = availableHeight;
            width = Math.Max(1, (int)Math.Floor(height * targetAspect));
        }

        return new GamePresentationViewport(
            (availableWidth - width) / 2,
            (availableHeight - height) / 2,
            width,
            height);
    }
}

/// <summary>
/// Stores one centered game-content region within a presentation surface.
/// </summary>
public readonly record struct GamePresentationViewport
{
    /// <summary>
    /// Creates a validated game presentation viewport.
    /// </summary>
    /// <param name="x">
    /// Non-negative horizontal offset.
    /// </param>
    /// <param name="y">
    /// Non-negative vertical offset.
    /// </param>
    /// <param name="width">
    /// Positive content width.
    /// </param>
    /// <param name="height">
    /// Positive content height.
    /// </param>
    public GamePresentationViewport(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }

    /// <summary>
    /// Gets the horizontal content offset.
    /// </summary>
    public int x { get; }

    /// <summary>
    /// Gets the vertical content offset.
    /// </summary>
    public int y { get; }

    /// <summary>
    /// Gets the content width.
    /// </summary>
    public int width { get; }

    /// <summary>
    /// Gets the content height.
    /// </summary>
    public int height { get; }
}
