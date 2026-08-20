using System;

namespace Inno.Editor.Panel.FileBrowser.AssetEditors;

/// <summary>Associates an asset editor with an imported asset type.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class AssetEditorAttribute : Attribute
{
    /// <summary>
    /// Creates an asset-editor registration for an imported runtime asset type.
    /// </summary>
    /// <param name="assetType">The imported asset type handled by the editor.</param>
    /// <param name="useForChildren">Whether the editor may also handle assignable derived asset types.</param>
    /// <param name="priority">The tie-breaking priority after exactness and inheritance distance.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assetType"/> is <see langword="null"/>.</exception>
    public AssetEditorAttribute(Type assetType, bool useForChildren = false, int priority = 0)
    {
        this.assetType = assetType ?? throw new ArgumentNullException(nameof(assetType));
        this.useForChildren = useForChildren;
        this.priority = priority;
    }

    /// <summary>Gets the imported asset type handled by the editor.</summary>
    public Type assetType { get; }

    /// <summary>Gets whether derived asset types are accepted.</summary>
    public bool useForChildren { get; }

    /// <summary>Gets the tie-breaking priority.</summary>
    public int priority { get; }
}
