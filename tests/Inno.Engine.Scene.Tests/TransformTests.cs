using System;

using Inno.Core.Mathematics;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;

using Xunit;

namespace Inno.Engine.Scene.Tests;

[Collection(SceneTestsCollection.NAME)]
public sealed class TransformTests : IDisposable
{
    public TransformTests(SceneTestsFixture _)
    {
    }

    public void Dispose()
    {
        SceneManager.UnloadActiveScene();
    }

    [Fact]
    public void SetParent_PreservesWorldTransform_AndComputesLocalWithNonUniformScale()
    {
        var scene = new GameScene("SetParentScale");
        Transform parent = scene.CreateObject("Parent").GetComponent<Transform>();
        Transform child = scene.CreateObject("Child").GetComponent<Transform>();

        parent.localPosition = new Vector3(2, 3, 4);
        parent.localRotation = Quaternion.identity;
        parent.localScale = new Vector3(2f, 3f, 4f);

        child.localPosition = new Vector3(1f, 2f, 3f);
        child.localRotation = Quaternion.CreateFromAxisAngle(new Vector3(0, 1, 0), 0.35f);
        child.localScale = new Vector3(2f, 2f, 2f);

        Vector3 worldPosition = child.worldPosition;
        Quaternion worldRotation = child.worldRotation;
        Vector3 worldScale = child.worldScale;

        child.SetParent(parent);

        Assert.Equal(worldPosition, child.worldPosition);
        Assert.Equal(worldRotation, child.worldRotation);
        Assert.Equal(worldScale, child.worldScale);

        Vector3 expectedLocalPosition = new Vector3(-0.5f, -0.33333334f, -0.25f);
        Vector3 expectedLocalScale = new Vector3(1f, 0.6666667f, 0.5f);

        AssertEqualWithTolerance(worldPosition, child.worldPosition, 1e-5f);
        AssertEqualWithTolerance(worldRotation.normalized, child.worldRotation.normalized, 1e-5f);
        AssertEqualWithTolerance(worldScale, child.worldScale, 1e-5f);

        AssertEqualWithTolerance(expectedLocalPosition, child.localPosition, 1e-5f);
        AssertEqualWithTolerance(expectedLocalScale, child.localScale, 1e-5f);
    }

    [Fact]
    public void SetParent_WithRotation_PreservesWorldRotation_WithLocalRotationByInverseParent()
    {
        var scene = new GameScene("SetParentRotation");
        Transform parent = scene.CreateObject("Parent").GetComponent<Transform>();
        Transform child = scene.CreateObject("Child").GetComponent<Transform>();

        parent.localPosition = new Vector3(3, 0, 0);
        parent.localRotation = Quaternion.CreateFromAxisAngle(new Vector3(0, 0, 1), MathF.PI / 2f);
        parent.localScale = Vector3.ONE;

        child.localPosition = new Vector3(1, 0, 0);
        child.localRotation = Quaternion.CreateFromAxisAngle(new Vector3(0, 1, 0), 0.2f);
        child.localScale = new Vector3(1, 1, 1);

        Vector3 worldPosition = child.worldPosition;
        Quaternion worldRotation = child.worldRotation;
        Vector3 worldScale = child.worldScale;

        child.SetParent(parent);

        Vector3 expectedLocalPosition = new Vector3(0f, 2f, 0f);
        Quaternion expectedLocalRotation = Quaternion.Inverse(parent.localRotation) * worldRotation;
        Quaternion normalizedExpectedRotation = expectedLocalRotation.normalized;

        Assert.Equal(worldPosition, child.worldPosition);
        AssertEqualWithTolerance(worldScale, child.worldScale, 1e-5f);
        Assert.Equal(Vector3.ONE, child.localScale);
        AssertEqualWithTolerance(expectedLocalPosition, child.localPosition, 1e-5f);
        AssertEqualWithTolerance(normalizedExpectedRotation, child.localRotation.normalized, 1e-5f);
    }

    [Fact]
    public void SetParent_UnparentPreservesWorldTransform()
    {
        var scene = new GameScene("Unparent");
        Transform parent = scene.CreateObject("Parent").GetComponent<Transform>();
        Transform child = scene.CreateObject("Child").GetComponent<Transform>();

        parent.localPosition = new Vector3(5, 6, 7);
        parent.localRotation = Quaternion.CreateFromAxisAngle(new Vector3(1, 0, 0), 0.4f);
        parent.localScale = new Vector3(2f, 3f, 4f);

        child.localPosition = new Vector3(1f, 1f, 1f);
        child.localRotation = Quaternion.CreateFromAxisAngle(new Vector3(0, 0, 1), 0.1f);
        child.localScale = new Vector3(0.5f, 0.5f, 0.5f);

        child.SetParent(parent);

        Vector3 preservedPosition = child.worldPosition;
        Quaternion preservedRotation = child.worldRotation;
        Vector3 preservedScale = child.worldScale;

        child.SetParent(null);

        Assert.Equal(preservedPosition, child.worldPosition);
        AssertEqualWithTolerance(preservedRotation.normalized, child.worldRotation.normalized, 1e-5f);
        Assert.Equal(preservedScale, child.worldScale);
        Assert.Null(child.parent);
        AssertEqualWithTolerance(preservedPosition, child.localPosition, 1e-5f);
        AssertEqualWithTolerance(preservedScale, child.localScale, 1e-5f);
    }

    [Fact]
    public void SetParent_RejectsSelfAndDescendantCycle()
    {
        var scene = new GameScene("Cycle");
        Transform root = scene.CreateObject("Root").GetComponent<Transform>();
        Transform child = scene.CreateObject("Child").GetComponent<Transform>();
        Transform grandChild = scene.CreateObject("GrandChild").GetComponent<Transform>();

        child.SetParent(root);
        grandChild.SetParent(child);

        Assert.Throws<InvalidOperationException>(() => child.SetParent(child));
        Assert.Throws<InvalidOperationException>(() => root.SetParent(grandChild));

        Assert.Same(root, child.parent);
        Assert.Same(child, grandChild.parent);
    }

    private static void AssertEqualWithTolerance(Vector3 expected, Vector3 actual, float tolerance)
    {
        float deltaX = expected.x - actual.x;
        float deltaY = expected.y - actual.y;
        float deltaZ = expected.z - actual.z;

        float distance = MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ));
        Assert.True(distance <= tolerance);
    }

    private static void AssertEqualWithTolerance(Quaternion expected, Quaternion actual, float tolerance)
    {
        float deltaX = expected.x - actual.x;
        float deltaY = expected.y - actual.y;
        float deltaZ = expected.z - actual.z;
        float deltaW = expected.w - actual.w;

        float distance = MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ) + (deltaW * deltaW));
        Assert.True(distance <= tolerance);
    }
}
