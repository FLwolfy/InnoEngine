using System;

namespace Inno.Adapter.Presentation;

/// <summary>
/// Identifies a renderer-owned texture through an opaque host-presentation token.
/// </summary>
public readonly record struct PresentationTextureHandle
{
    /// <summary>
    /// Creates an opaque presentation texture token.
    /// </summary>
    /// <param name="value">
    /// Non-zero token allocated by the active presentation backend.
    /// </param>
    public PresentationTextureHandle(ulong value)
    {
        if (value == 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        this.value = value;
    }

    /// <summary>
    /// Gets the opaque backend token.
    /// </summary>
    public ulong value { get; }

    /// <summary>
    /// Gets whether this token identifies a registered presentation texture.
    /// </summary>
    public bool isValid => value != 0;
}
