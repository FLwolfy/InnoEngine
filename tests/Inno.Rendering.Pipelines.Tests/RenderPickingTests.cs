using System;
using Inno.Core.Mathematics;
using Inno.Engine.Scene.Layers;
using Xunit;

namespace Inno.Rendering.Pipelines.Tests;

public sealed class RenderPickingTests
{
    [Fact]
    public void TryPickBounds_SelectsNearestVisibleRenderer()
    {
        Guid nearestId = Guid.NewGuid();
        Guid fartherId = Guid.NewGuid();
        RenderObjectData[] objects =
        [
            CreateObject(fartherId, new Vector3(0f, 0f, 4f)),
            CreateObject(nearestId, Vector3.ZERO),
            CreateObject(Guid.NewGuid(), new Vector3(20f, 0f, 0f))
        ];
        Vector3 cameraPosition = new(0f, 0f, -5f);
        var view = new RenderView(
            Matrix.CreateLookAt(cameraPosition, Vector3.ZERO, Vector3.UP),
            Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(60f), 1f, 0.1f, 100f),
            cameraPosition,
            512,
            512,
            GameLayerMask.everything);

        bool picked = RenderPicking.TryPickBounds(objects, view, 0.5f, 0.5f, out Guid rendererId);

        Assert.True(picked);
        Assert.Equal(nearestId, rendererId);
    }

    [Fact]
    public void EncodeObjectId_MatchesPersistentGuidByteContract()
    {
        Guid id = Guid.Parse("01020304-0506-0708-090a-0b0c0d0e0f10");
        byte[] bytes = id.ToByteArray();

        Vector4 encoded = RenderPicking.EncodeObjectId(id);

        Assert.Equal(bytes[0] / 255f, encoded.x);
        Assert.Equal(bytes[1] / 255f, encoded.y);
        Assert.Equal(bytes[2] / 255f, encoded.z);
        Assert.Equal(bytes[3] / 255f, encoded.w);
    }

    private static RenderObjectData CreateObject(Guid id, Vector3 center)
        => new(
            id,
            GameLayer.defaultLayer,
            Matrix.CreateTranslation(center),
            new RenderBounds(center, Vector3.ONE),
            new MeshAsset(),
            Array.Empty<MaterialAsset>(),
            null,
            2000,
            transparent: false,
            ShadowCastingMode.On,
            receiveShadows: true,
            enableInstancing: true);
}
