using System.IO;

using Inno.Assets.AssetType;
using Inno.Core.Logging;
using Inno.Core.Math;
using Inno.Graphics.Resources.CpuResources;
using Inno.Platform.Graphics;

namespace Inno.Graphics.Decoder;

internal sealed class MeshDecoder : ResourceDecoder<Mesh, MeshAsset>
{
    protected override Mesh OnDecode(MeshAsset asset)
    {
        using var ms = new MemoryStream(asset.assetBinaries);
        using var br = new BinaryReader(ms);

        uint magic = br.ReadUInt32();
        if (magic != 0x48534D49u) // 'IMSH'
            throw new InvalidDataException($"MeshAsset bin magic mismatch: 0x{magic:X8}");

        uint ver = br.ReadUInt32();
        if (ver != 1u)
            throw new InvalidDataException($"Unsupported MeshAsset bin version: {ver}");

        var topology = (PrimitiveTopology)br.ReadUInt32();
        int vCount = (int)br.ReadUInt32();
        int iCount = (int)br.ReadUInt32();

        bool hasNormals = br.ReadUInt32() != 0;
        bool hasUV = br.ReadUInt32() != 0;

        var mesh = new Mesh(asset.guid, asset.name);
        mesh.renderState = new MeshRenderState { topology = topology };

        var pos = new Vector3[vCount];
        for (int i = 0; i < vCount; i++)
            pos[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        mesh.SetAttribute("Position", pos);

        if (hasNormals)
        {
            var nrm = new Vector3[vCount];
            for (int i = 0; i < vCount; i++)
                nrm[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            mesh.SetAttribute("Normal", nrm);
        }

        if (hasUV)
        {
            var uv = new Vector2[vCount];
            for (int i = 0; i < vCount; i++)
                uv[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
            mesh.SetAttribute("TexCoord0", uv);
        }

        var indices = new uint[iCount];
        for (int i = 0; i < iCount; i++)
            indices[i] = br.ReadUInt32();
        mesh.SetIndices(indices);
        
        // TODO
        // Implement Segments
        return mesh;
    }
}
