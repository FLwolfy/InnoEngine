using System;

namespace Inno.Editor.Assets.AssetEditors;

/// <summary>Associates an asset editor with an imported asset type.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class AssetEditorAttribute : Attribute
{
    /// <summary>Creates an asset editor registration.</summary>
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
