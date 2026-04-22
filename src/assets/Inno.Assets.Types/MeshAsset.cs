using Inno.Assets.Core;
using Inno.Platform.Graphics;

namespace Inno.Assets.Types;

public sealed class MeshAsset : InnoAsset
{
    [AssetProperty] public int vertexCount { get; private set; }
    [AssetProperty] public int indexCount { get; private set; }
    [AssetProperty] public PrimitiveTopology topology { get; private set; } = PrimitiveTopology.TriangleList;

    public MeshAsset(int vertexCount, int indexCount, PrimitiveTopology topology)
    {
        this.vertexCount = vertexCount;
        this.indexCount = indexCount;
        this.topology = topology;
    }
}