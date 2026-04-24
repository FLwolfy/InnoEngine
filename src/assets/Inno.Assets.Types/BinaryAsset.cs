using Inno.Assets.Core;
using Inno.Core.Serialization;

namespace Inno.Assets.Types;

public sealed class BinaryAsset : AssetObject
{
    [SerializableProperty]
    public int byteLength { get; private set; }

    public BinaryAsset()
    {
    }

    public BinaryAsset(int byteLength)
    {
        this.byteLength = byteLength;
    }
}
