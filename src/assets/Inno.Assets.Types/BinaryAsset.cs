using Inno.Assets.Core;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

namespace Inno.Assets.Types;

/// <summary>
/// Describes an imported opaque binary payload.
/// </summary>
[StableTypeId("5298dd91-e9a7-4298-a343-bf8a6c5fc779")]
public sealed class BinaryAsset : AssetObject
{
    /// <summary>
    /// Gets the imported payload length in bytes.
    /// </summary>
    [SerializableProperty]
    public int byteLength { get; private set; }

    /// <summary>
    /// Creates an empty binary asset descriptor.
    /// </summary>
    public BinaryAsset()
    {
    }

    /// <summary>
    /// Creates a binary asset descriptor with a byte length.
    /// </summary>
    /// <param name="byteLength">Payload length in bytes.</param>
    public BinaryAsset(int byteLength)
    {
        this.byteLength = byteLength;
    }
}
