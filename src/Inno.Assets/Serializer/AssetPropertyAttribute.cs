using System;

namespace Inno.Assets.Serializer;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class AssetPropertyAttribute : Attribute
{
    /// <summary>
    /// Indicates whether the asset property is read-only in editor.
    /// </summary>
    public bool readOnly { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetPropertyAttribute"/> class.
    /// </summary>
    /// <param name="readOnly">Whether the property is read-only in editor.</param>
    public AssetPropertyAttribute(bool readOnly = false)
    {
        this.readOnly = readOnly;
    }
}