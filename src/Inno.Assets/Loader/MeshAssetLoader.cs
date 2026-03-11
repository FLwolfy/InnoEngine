using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using Inno.Assets.AssetType;
using Inno.Core.Math;
using Inno.Platform.Graphics;

namespace Inno.Assets.Loader;

internal sealed class MeshAssetLoader : InnoAssetLoader<MeshAsset>
{
    public override string[] validExtensions => [".obj"];

    protected override byte[] OnLoadBinaries(string assetName, byte[] rawBytes, out MeshAsset asset)
    {
        var objText = Encoding.UTF8.GetString(rawBytes);
        var imported = ObjImporter.Import(objText, assetName);

        asset = new MeshAsset(
            vertexCount: imported.positions.Length,
            indexCount: imported.indices.Length,
            topology: PrimitiveTopology.TriangleList
        );

        return MeshBinWriter.Write(
            topology: PrimitiveTopology.TriangleList,
            positions: imported.positions,
            normals: imported.normals,
            uvs: imported.uvs,
            indices: imported.indices
        );
    }

    protected override byte[] OnSaveSource(string assetName, in MeshAsset asset)
        => throw new NotSupportedException("Saving mesh source back to .obj is not supported yet.");

    // -------------------- OBJ Import (minimal) --------------------
    private static class ObjImporter
    {
        private readonly struct FaceIndex(int v, int vt, int vn)
        {
            public readonly int v = v;   // 0-based, -1 if missing
            public readonly int vt = vt; // 0-based, -1 if missing
            public readonly int vn = vn; // 0-based, -1 if missing
        }

        internal readonly struct Imported(Vector3[] positions, Vector3[] normals, Vector2[] uvs, uint[] indices)
        {
            public readonly Vector3[] positions = positions;
            public readonly Vector3[] normals = normals;
            public readonly Vector2[] uvs = uvs;
            public readonly uint[] indices = indices;
        }

        public static Imported Import(string text, string name)
        {
            var posPool = new List<Vector3>();
            var uvPool  = new List<Vector2>();
            var nrmPool = new List<Vector3>();

            var outPos = new List<Vector3>();
            var outUv  = new List<Vector2>();
            var outNrm = new List<Vector3>();
            var outIdx = new List<uint>();

            // key: (v,vt,vn) -> output vertex index
            var map = new Dictionary<(int v, int vt, int vn), uint>();

            using var sr = new StringReader(text);
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                // split (avoid allocations too much? fine for now)
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                switch (parts[0])
                {
                    case "v":
                        // v x y z
                        posPool.Add(new Vector3(
                            ParseF(parts, 1), ParseF(parts, 2), ParseF(parts, 3)));
                        break;

                    case "vt":
                        // vt u v  (OBJ v is usually bottom-up; keep as-is, you can flip later in shader)
                        uvPool.Add(new Vector2(
                            ParseF(parts, 1), ParseF(parts, 2)));
                        break;

                    case "vn":
                        nrmPool.Add(new Vector3(
                            ParseF(parts, 1), ParseF(parts, 2), ParseF(parts, 3)));
                        break;

                    case "f":
                        // f a b c [d ...]  (triangulate via fan)
                        // token formats: v, v/vt, v//vn, v/vt/vn
                        if (parts.Length < 4) break;

                        var face = new FaceIndex[parts.Length - 1];
                        for (int i = 1; i < parts.Length; i++)
                            face[i - 1] = ParseFaceIndex(parts[i], posPool.Count, uvPool.Count, nrmPool.Count);

                        // triangulate: (0, i, i+1)
                        for (int i = 1; i + 1 < face.Length; i++)
                        {
                            Emit(face[0]);
                            Emit(face[i]);
                            Emit(face[i + 1]);
                        }
                        break;
                }
            }

            // if missing normals/uvs, output arrays as empty
            Vector3[] normals = outNrm.Count == outPos.Count ? outNrm.ToArray() : [];
            Vector2[] uvs     = outUv.Count == outPos.Count ? outUv.ToArray() : [];

            return new Imported(outPos.ToArray(), normals, uvs, outIdx.ToArray());

            void Emit(FaceIndex fi)
            {
                var key = (fi.v, fi.vt, fi.vn);
                if (!map.TryGetValue(key, out uint outIndex))
                {
                    outIndex = (uint)outPos.Count;
                    map[key] = outIndex;

                    outPos.Add(posPool[fi.v]);

                    if (fi.vt >= 0) outUv.Add(uvPool[fi.vt]);
                    if (fi.vn >= 0) outNrm.Add(nrmPool[fi.vn]);
                }

                outIdx.Add(outIndex);
            }
        }

        private static FaceIndex ParseFaceIndex(string token, int vCount, int vtCount, int vnCount)
        {
            // OBJ indices are 1-based; negative means relative to end.
            // token can be: "v", "v/vt", "v//vn", "v/vt/vn"
            int v = -1, vt = -1, vn = -1;

            var seg = token.Split('/', StringSplitOptions.None);
            if (seg.Length >= 1 && seg[0].Length > 0) v  = FixIndex(int.Parse(seg[0], CultureInfo.InvariantCulture), vCount);
            if (seg.Length >= 2 && seg[1].Length > 0) vt = FixIndex(int.Parse(seg[1], CultureInfo.InvariantCulture), vtCount);
            if (seg.Length >= 3 && seg[2].Length > 0) vn = FixIndex(int.Parse(seg[2], CultureInfo.InvariantCulture), vnCount);

            if (v < 0) throw new Exception($"OBJ face index missing position: '{token}'");
            return new FaceIndex(v, vt, vn);
        }

        private static int FixIndex(int objIndex, int count)
        {
            // objIndex: 1..N or -1..-N
            if (objIndex > 0) return objIndex - 1;
            if (objIndex < 0) return count + objIndex;
            return -1;
        }

        private static float ParseF(string[] parts, int i)
            => float.Parse(parts[i], CultureInfo.InvariantCulture);
    }

    // -------------------- Mesh bin format writer --------------------
    private static class MeshBinWriter
    {
        // v0 format (little endian):
        // [u32 magic 'IMSH'] [u32 version=1]
        // [u32 topology]
        // [u32 vertexCount] [u32 indexCount]
        // [u32 hasNormals 0/1] [u32 hasUV 0/1]
        // positions: vertexCount * (3*f32)
        // normals:   vertexCount * (3*f32) if hasNormals
        // uvs:       vertexCount * (2*f32) if hasUV
        // indices:   indexCount * (u32)
        public static byte[] Write(
            PrimitiveTopology topology,
            Vector3[] positions,
            Vector3[] normals,
            Vector2[] uvs,
            uint[] indices)
        {
            bool hasNormals = normals != null && normals.Length == positions.Length && normals.Length > 0;
            bool hasUV = uvs != null && uvs.Length == positions.Length && uvs.Length > 0;

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            bw.Write(0x48534D49u); // 'IMSH'
            bw.Write(1u);          // version
            bw.Write((uint)topology);

            bw.Write((uint)positions.Length);
            bw.Write((uint)indices.Length);

            bw.Write(hasNormals ? 1u : 0u);
            bw.Write(hasUV ? 1u : 0u);

            for (int i = 0; i < positions.Length; i++)
            {
                bw.Write(positions[i].x);
                bw.Write(positions[i].y);
                bw.Write(positions[i].z);
            }

            if (hasNormals)
            {
                for (int i = 0; i < normals.Length; i++)
                {
                    bw.Write(normals[i].x);
                    bw.Write(normals[i].y);
                    bw.Write(normals[i].z);
                }
            }

            if (hasUV)
            {
                for (int i = 0; i < uvs.Length; i++)
                {
                    bw.Write(uvs[i].x);
                    bw.Write(uvs[i].y);
                }
            }

            for (int i = 0; i < indices.Length; i++)
                bw.Write(indices[i]);

            bw.Flush();
            return ms.ToArray();
        }
    }
}
