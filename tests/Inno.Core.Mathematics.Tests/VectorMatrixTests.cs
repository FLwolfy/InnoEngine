using System;

using Inno.Core.Mathematics;

using Xunit;

namespace Inno.Core.Mathematics.Tests;

public sealed class VectorMatrixTests
{
    [Fact]
    public void Vector2QuaternionTransform_PreservesLengthForZRotation()
    {
        Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.FORWARD, MathF.PI * 0.5f);

        Vector2 result = Vector2.Transform(Vector2.UNIT_X, rotation);

        Assert.InRange(result.x, -0.00001f, 0.00001f);
        Assert.InRange(result.y, 0.99999f, 1.00001f);
        Assert.InRange(result.Length(), 0.99999f, 1.00001f);
    }

    [Fact]
    public void EulerAnglesXYZ_RoundTripsCompoundRotation()
    {
        var source = new Vector3(0.31f, -0.47f, 0.83f);

        Quaternion original = Quaternion.FromEulerAnglesXYZ(source).normalized;
        Quaternion roundTrip = Quaternion.FromEulerAnglesXYZ(original.ToEulerAnglesXYZ()).normalized;

        float alignment = MathF.Abs(
            original.x * roundTrip.x
            + original.y * roundTrip.y
            + original.z * roundTrip.z
            + original.w * roundTrip.w);
        Assert.InRange(alignment, 0.99999f, 1.00001f);
    }

    [Fact]
    public void Vector2_LengthSquared_MatchesDot()
    {
        var v = new Vector2(3f, 4f);
        var dot = Vector2.Dot(v, v);
        Assert.True(MathHelper.AlmostEquals(25f, dot));
        Assert.True(MathHelper.AlmostEquals(25f, v.LengthSquared()));
        Assert.True(MathHelper.AlmostEquals(5f, v.Length()));
    }

    [Fact]
    public void Vector3_Cross_IsOrthogonal()
    {
        var a = new Vector3(1f, 0f, 0f);
        var b = new Vector3(0f, 1f, 0f);
        var c = Vector3.Cross(a, b);

        Assert.True(MathHelper.AlmostEquals(0f, Vector3.Dot(a, c)));
        Assert.True(MathHelper.AlmostEquals(0f, Vector3.Dot(b, c)));
        Assert.True(MathHelper.AlmostEquals(0f, c.x));
        Assert.True(MathHelper.AlmostEquals(0f, c.y));
        Assert.True(MathHelper.AlmostEquals(1f, c.z));
    }

    [Fact]
    public void Vector3_TransformNormal_IgnoresTranslation()
    {
        var normal = new Vector3(0f, 1f, 0f);
        var translation = Matrix.CreateTranslation(10f, 20f, 30f);
        var result = Vector3.TransformNormal(normal, translation);

        Assert.True(MathHelper.AlmostEquals(normal.x, result.x));
        Assert.True(MathHelper.AlmostEquals(normal.y, result.y));
        Assert.True(MathHelper.AlmostEquals(normal.z, result.z));
    }

    [Fact]
    public void Vector2_Angle_SignedAngle()
    {
        var right = new Vector2(1f, 0f);
        var up = new Vector2(0f, 1f);
        var angle = Vector2.Angle(right, up);
        var signed = Vector2.SignedAngle(right, up);

        Assert.True(MathHelper.AlmostEquals(MathF.PI * 0.5f, angle));
        Assert.True(MathHelper.AlmostEquals(MathF.PI * 0.5f, signed));
    }

    [Fact]
    public void Matrix_Multiply_ColumnVector_Order()
    {
        var scale = Matrix.CreateScale(2f);
        var translate = Matrix.CreateTranslation(1f, 0f, 0f);
        var combined = translate * scale;

        var v = new Vector3(1f, 0f, 0f);
        var result = Vector3.Transform(v, combined);

        Assert.True(MathHelper.AlmostEquals(3f, result.x));
        Assert.True(MathHelper.AlmostEquals(0f, result.y));
        Assert.True(MathHelper.AlmostEquals(0f, result.z));
    }

    [Fact]
    public void Matrix_Determinant_Invert_Roundtrip()
    {
        var m = Matrix.CreateTranslation(1f, 2f, 3f) * Matrix.CreateScale(2f, 3f, 4f);
        var det = Matrix.Determinant(m);
        Assert.True(MathHelper.AlmostEquals(24f, det));

        var inv = Matrix.Invert(m);
        var identity = m * inv;
        Assert.True(identity == Matrix.identity);
    }

    [Fact]
    public void Matrix_Decompose_Roundtrip()
    {
        var scale = new Vector3(2f, 3f, 4f);
        var rotation = Quaternion.FromEulerAnglesXYZ(new Vector3(0.2f, 0.3f, 0.4f));
        var translation = new Vector3(5f, 6f, 7f);
        var m = Matrix.CreateTranslation(translation) * Matrix.CreateFromQuaternion(rotation) * Matrix.CreateScale(scale);

        var success = Matrix.Decompose(m, out var outScale, out var outRotation, out var outTranslation);
        Assert.True(success);
        Assert.True(outTranslation == translation);
        Assert.True(MathHelper.AlmostEquals(scale.x, outScale.x));
        Assert.True(MathHelper.AlmostEquals(scale.y, outScale.y));
        Assert.True(MathHelper.AlmostEquals(scale.z, outScale.z));

        var rotMatrix = outRotation.ToMatrix();
        var expected = rotation.ToMatrix();
        Assert.True(rotMatrix == expected);
    }

    [Fact]
    public void Matrix_LookAtRH_IdentityForMinusZ()
    {
        var eye = new Vector3(0f, 0f, 0f);
        var target = new Vector3(0f, 0f, -1f);
        var up = new Vector3(0f, 1f, 0f);
        var view = Matrix.CreateLookAtRH(eye, target, up);

        Assert.True(view == Matrix.identity);
    }

    [Fact]
    public void Matrix_PerspectiveRH_MapsNearFar()
    {
        float near = 0.1f;
        float far = 10f;
        var proj = Matrix.CreatePerspectiveFieldOfViewRH(MathF.PI / 2f, 1f, near, far);

        var vNear = new Vector4(0f, 0f, -near, 1f);
        var vFar = new Vector4(0f, 0f, -far, 1f);

        var pNear = Vector4.Transform(vNear, proj).ProjectToVector3();
        var pFar = Vector4.Transform(vFar, proj).ProjectToVector3();

        Assert.True(MathHelper.AlmostEquals(0f, pNear.z));
        Assert.True(MathHelper.AlmostEquals(1f, pFar.z));
    }

    [Fact]
    public void Matrix_PerspectiveLH_MapsNearFar()
    {
        float near = 0.1f;
        float far = 10f;
        var projection = Matrix.CreatePerspectiveFieldOfView(MathF.PI / 2f, 1f, near, far);

        Vector3 projectedNear = (projection * new Vector4(0f, 0f, near, 1f)).ProjectToVector3();
        Vector3 projectedFar = (projection * new Vector4(0f, 0f, far, 1f)).ProjectToVector3();

        Assert.True(MathHelper.AlmostEquals(0f, projectedNear.z));
        Assert.True(MathHelper.AlmostEquals(1f, projectedFar.z));
    }

    [Fact]
    public void Matrix_LookAtLH_RotatesWorldIntoCameraAxes()
    {
        Matrix view = Matrix.CreateLookAt(Vector3.ZERO, Vector3.RIGHT, Vector3.UP);

        Vector3 forward = Vector3.Transform(Vector3.RIGHT, view);
        Vector3 right = Vector3.Transform(Vector3.BACK, view);

        Assert.True(forward == Vector3.FORWARD);
        Assert.True(right == Vector3.RIGHT);
    }

    [Fact]
    public void Matrix_OrthographicOffCenter_MapsEdges()
    {
        var ortho = Matrix.CreateOrthographicOffCenter(-2f, 2f, -1f, 1f, 0f, 10f);

        var left = Vector4.Transform(new Vector4(-2f, 0f, 0f, 1f), ortho).ProjectToVector3();
        var right = Vector4.Transform(new Vector4(2f, 0f, 0f, 1f), ortho).ProjectToVector3();

        Assert.True(MathHelper.AlmostEquals(-1f, left.x));
        Assert.True(MathHelper.AlmostEquals(1f, right.x));
    }

    [Fact]
    public void Matrix_ToColumnMajorArray_Layout()
    {
        var m = new Matrix(
            1f, 2f, 3f, 4f,
            5f, 6f, 7f, 8f,
            9f, 10f, 11f, 12f,
            13f, 14f, 15f, 16f);

        var data = m.ToColumnMajorArray();

        Assert.Equal(16, data.Length);
        Assert.True(MathHelper.AlmostEquals(1f, data[0]));
        Assert.True(MathHelper.AlmostEquals(5f, data[1]));
        Assert.True(MathHelper.AlmostEquals(9f, data[2]));
        Assert.True(MathHelper.AlmostEquals(13f, data[3]));
        Assert.True(MathHelper.AlmostEquals(2f, data[4]));
        Assert.True(MathHelper.AlmostEquals(6f, data[5]));
        Assert.True(MathHelper.AlmostEquals(10f, data[6]));
        Assert.True(MathHelper.AlmostEquals(14f, data[7]));
    }

    [Fact]
    public void Quaternion_Normalize_UnitLength()
    {
        var q = new Quaternion(1f, 2f, 3f, 4f);
        var n = Quaternion.Normalize(q);
        var len = n.Length();
        Assert.True(MathHelper.AlmostEquals(1f, len));
    }

    [Fact]
    public void Quaternion_FromRotationMatrix_Roundtrip()
    {
        var q = Quaternion.FromEulerAnglesXYZ(new Vector3(0.25f, 0.5f, 1.1f));
        var m = q.ToMatrix();
        var q2 = Quaternion.FromRotationMatrix(m);
        if (Dot(q, q2) < 0f)
        {
            q2 = new Quaternion(-q2.x, -q2.y, -q2.z, -q2.w);
        }

        const float epsilon = 1e-3f;
        Assert.True(MathF.Abs(q.x - q2.x) < epsilon);
        Assert.True(MathF.Abs(q.y - q2.y) < epsilon);
        Assert.True(MathF.Abs(q.z - q2.z) < epsilon);
        Assert.True(MathF.Abs(q.w - q2.w) < epsilon);
    }

    private static float Dot(Quaternion a, Quaternion b)
    {
        return a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
    }

    [Fact]
    public void MathHelper_Clamp_Saturate_IsFinite()
    {
        Assert.True(MathHelper.AlmostEquals(0.5f, MathHelper.Clamp(0.5f, 0f, 1f)));
        Assert.True(MathHelper.AlmostEquals(1f, MathHelper.Clamp(2f, 0f, 1f)));
        Assert.True(MathHelper.AlmostEquals(0f, MathHelper.Saturate(-2f)));
        Assert.True(MathHelper.IsFinite(1f));
        Assert.False(MathHelper.IsFinite(float.PositiveInfinity));
    }
}
