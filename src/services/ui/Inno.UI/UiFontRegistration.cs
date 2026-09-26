using System;

using Inno.Text;

namespace Inno.UI;

/// <summary>
/// Describes one encoded font face registration across the neutral backend boundary.
/// </summary>
public readonly record struct UiFontRegistration
{
    /// <summary>
    /// Creates a validated synchronous font registration request.
    /// </summary>
    /// <param name="data">
    /// Encoded font bytes, valid for the duration of the backend call.
    /// </param>
    /// <param name="faceIndex">
    /// Zero-based face index in a font collection.
    /// </param>
    /// <param name="family">
    /// Logical family visible to document authors.
    /// </param>
    /// <param name="style">
    /// Requested logical font style.
    /// </param>
    /// <param name="weight">
    /// CSS-compatible weight from 100 through 1000.
    /// </param>
    public UiFontRegistration(
        ReadOnlyMemory<byte> data,
        int faceIndex,
        string family,
        TextFontStyle style,
        int weight)
    {
        if (data.IsEmpty) throw new ArgumentException("Font data cannot be empty.", nameof(data));
        ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        if (weight is < 100 or > 1000) throw new ArgumentOutOfRangeException(nameof(weight));
        this.data = data;
        this.faceIndex = faceIndex;
        this.family = family.Trim();
        this.style = style;
        this.weight = weight;
    }

    /// <summary>
    /// Gets encoded font bytes valid for the synchronous backend call.
    /// </summary>
    public ReadOnlyMemory<byte> data { get; }
    /// <summary>
    /// Gets the zero-based collection face index.
    /// </summary>
    public int faceIndex { get; }
    /// <summary>
    /// Gets the logical family.
    /// </summary>
    public string family { get; }
    /// <summary>
    /// Gets the logical style.
    /// </summary>
    public TextFontStyle style { get; }
    /// <summary>
    /// Gets the logical weight.
    /// </summary>
    public int weight { get; }
}
