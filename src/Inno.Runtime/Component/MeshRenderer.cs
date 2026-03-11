using Inno.Assets;
using Inno.Assets.AssetType;
using Inno.Assets.Core;
using Inno.Core.Math;
using Inno.Core.Serialization;
using Inno.Graphics.Decoder;
using Inno.Graphics.Resources.CpuResources;
using Inno.Core.ECS;

namespace Inno.Runtime.Component;

/// <summary>
/// MeshRenderer component draws a 3D mesh (currently unlit + solid color).
/// </summary>
public class MeshRenderer : GameBehavior
{
    public override ComponentTag orderTag => ComponentTag.Render;

    public Mesh? mesh { get; private set; }

    [SerializableProperty]
    private AssetRef<MeshAsset> source
    {
        get
        {
            if (mesh == null)
            {
                return default;
            }
            
            return AssetManager.Get<MeshAsset>(mesh.guid);
        }
        set
        { 
            if (value.isValid)
            {
                var asset = value.Resolve();
                if (asset != null)
                {
                    mesh = ResourceDecoder.DecodeBinaries<Mesh, MeshAsset>(asset);
                }
            }
            else
            {
                mesh = null;
            }
        }
    }

    /// <summary>
    /// Tint color (unlit).
    /// </summary>
    [SerializableProperty]
    public Color color { get; set; } = Color.WHITE;
}