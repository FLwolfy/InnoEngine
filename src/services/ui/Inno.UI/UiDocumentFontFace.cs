using System;
using Inno.Text;

namespace Inno.UI;

/// <summary>
/// Identifies one imported font face owned by a single UI document.
/// </summary>
public readonly record struct UiDocumentFontFace
{
    /// <summary>
    /// Creates a font dependency with its document-private family.
    /// </summary>
    /// <param name="assetId">
    /// Persistent font asset identity.
    /// </param>
    /// <param name="family">
    /// Private family referenced by canonical document text.
    /// </param>
    /// <param name="style">
    /// Declared face style.
    /// </param>
    /// <param name="weight">
    /// Declared face weight.
    /// </param>
    public UiDocumentFontFace(Guid assetId, string family, TextFontStyle style, int weight)
    {
        if (assetId == Guid.Empty) throw new ArgumentException("A font asset ID is required.", nameof(assetId));
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        if (weight is < 100 or > 1000) throw new ArgumentOutOfRangeException(nameof(weight));
        this.assetId = assetId;
        this.family = family;
        this.style = style;
        this.weight = weight;
    }

    /// <summary>
    /// Gets the imported font asset identity.
    /// </summary>
    public Guid assetId { get; }
    /// <summary>
    /// Gets the family referenced by the document.
    /// </summary>
    public string family { get; }
    /// <summary>
    /// Gets the declared style.
    /// </summary>
    public TextFontStyle style { get; }
    /// <summary>
    /// Gets the declared weight.
    /// </summary>
    public int weight { get; }
}
