using Inno.Editor.Assets.DragDrop;

namespace Inno.Editor.Assets.DragDrop;

/// <summary>Identifies an asset browser directory drop target.</summary>
public sealed class AssetDirectoryDropTarget
{
    /// <summary>Creates an asset directory drop target.</summary>
    public AssetDirectoryDropTarget(string relativePath)
    {
        this.relativePath = relativePath ?? throw new System.ArgumentNullException(nameof(relativePath));
    }

    /// <summary>Gets the source-relative directory path.</summary>
    public string relativePath { get; }
}
