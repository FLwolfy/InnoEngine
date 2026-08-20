using Inno.Editor.Assets.DragDrop;

namespace Inno.Editor.Assets.DragDrop;

/// <summary>Identifies an asset browser directory drop target.</summary>
public sealed class AssetDirectoryDropTarget
{
    /// <summary>
    /// Creates a drop target representing one directory in the Asset source tree.
    /// </summary>
    /// <param name="relativePath">The normalized path relative to the Asset root.</param>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="relativePath"/> is <see langword="null"/>.</exception>
    public AssetDirectoryDropTarget(string relativePath)
    {
        this.relativePath = relativePath ?? throw new System.ArgumentNullException(nameof(relativePath));
    }

    /// <summary>Gets the source-relative directory path.</summary>
    public string relativePath { get; }
}
