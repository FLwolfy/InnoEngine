using Inno.Assets.Serializer;
using Inno.Platform.Graphics;

namespace Inno.Assets.AssetType;

public sealed class MeshAsset : InnoAsset
{
    [AssetProperty] public int vertexCount { get; private set; }
    [AssetProperty] public int indexCount { get; private set; }
    [AssetProperty] public PrimitiveTopology topology { get; private set; } = PrimitiveTopology.TriangleList;

    internal MeshAsset(int vertexCount, int indexCount, PrimitiveTopology topology)
    {
        this.vertexCount = vertexCount;
        this.indexCount = indexCount;
        this.topology = topology;
    }
}