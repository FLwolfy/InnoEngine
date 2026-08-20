using System;

namespace Inno.Editor.ImGui.Widgets;

/// <summary>Describes the view-owned geometry used to present an active inline rename action.</summary>
public sealed class InlineRenamePresentation
{
    /// <summary>Creates inline rename presentation data.</summary>
    /// <param name="id">The stable ImGui identifier used by the input field.</param>
    /// <param name="width">The requested input width in logical pixels.</param>
    /// <param name="bufferSize">The maximum UTF-8 buffer size accepted by the input.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is empty, <paramref name="width"/> is not positive, or <paramref name="bufferSize"/> is zero.</exception>
    public InlineRenamePresentation(string id, float width, nuint bufferSize = 512)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("An inline rename identifier is required.", nameof(id));
        if (width <= 0f)
            throw new ArgumentException("The inline rename width must be positive.", nameof(width));
        if (bufferSize == 0)
            throw new ArgumentException("The inline rename buffer size must be positive.", nameof(bufferSize));
        this.id = id;
        this.width = width;
        this.bufferSize = bufferSize;
    }

    /// <summary>Gets the stable ImGui identifier used by the input field.</summary>
    public string id { get; }

    /// <summary>Gets the requested input width in logical pixels.</summary>
    public float width { get; }

    /// <summary>Gets the maximum UTF-8 buffer size accepted by the input.</summary>
    public nuint bufferSize { get; }
}
