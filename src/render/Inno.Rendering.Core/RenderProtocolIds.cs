using System;

namespace Inno.Rendering.Core;

/// <summary>
/// Identifies a pipeline-defined semantic resource without imposing a central resource catalog.
/// </summary>
public readonly record struct RenderResourceId
{
    /// <summary>Creates an open semantic resource identifier.</summary>
    /// <param name="value">Globally stable resource protocol value.</param>
    public RenderResourceId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>Gets the globally stable protocol value.</summary>
    public string value { get; }

    /// <summary>Gets whether the identifier contains a protocol value.</summary>
    public bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <inheritdoc />
    public override string ToString() => value;
}

/// <summary>
/// Identifies one pipeline-owned frame-data channel without constraining its payload model.
/// </summary>
public readonly record struct RenderDataChannelId
{
    /// <summary>Creates an open frame-data channel identifier.</summary>
    /// <param name="value">Globally stable channel protocol value.</param>
    public RenderDataChannelId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>Gets the globally stable protocol value.</summary>
    public string value { get; }

    /// <summary>Gets whether the identifier contains a protocol value.</summary>
    public bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <inheritdoc />
    public override string ToString() => value;
}
