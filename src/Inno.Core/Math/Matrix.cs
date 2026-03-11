using System;
using System.Runtime.Serialization;

namespace Inno.Core.Math;

/// <remarks>
/// Conventions used by this type:
/// <list type="bullet">
///   <item><description><b>Row-major storage</b> (m11..m14 is first row, m41..m44 is last row).</description></item>
///   <item><description><b>Row-vector math</b>: points/vectors are treated as row vectors on the left: <c>v' = v * M</c>.</description></item>
///   <item><description><b>Translation is stored in the last row</b>: (m41, m42, m43).</description></item>
///   <item><description>Matrix multiplication follows standard form: <c>C = A * B</c> (apply A then B in row-vector convention: <c>v * A * B</c>).</description></item>
/// </list>
/// <para>
/// Note on handedness:
/// This <see cref="Matrix"/> type is <b>handedness-agnostic</b>. Handedness is determined by the specific factory
/// (e.g. <see cref="CreateLookAt"/> and <see cref="CreatePerspectiveFieldOfView"/> are currently <b>right-handed</b>).
/// If your engine convention is left-handed (+Z forward), add/use explicit LH variants (e.g. CreateLookAtLH / CreatePerspectiveFieldOfViewLH).
/// </para>
/// </remarks>
[DataContract]
public struct Matrix : IEquatable<Matrix>
{
    #region Data (Row-major)

    /// <summary>Row 1, Column 1.</summary>
    [DataMember] public float m11;
    /// <summary>Row 1, Column 2.</summary>
    [DataMember] public float m12;
    /// <summary>Row 1, Column 3.</summary>
    [DataMember] public float m13;
    /// <summary>Row 1, Column 4.</summary>
    [DataMember] public float m14;

    /// <summary>Row 2, Column 1.</summary>
    [DataMember] public float m21;
    /// <summary>Row 2, Column 2.</summary>
    [DataMember] public float m22;
    /// <summary>Row 2, Column 3.</summary>
    [DataMember] public float m23;
    /// <summary>Row 2, Column 4.</summary>
    [DataMember] public float m24;

    /// <summary>Row 3, Column 1.</summary>
    [DataMember] public float m31;
    /// <summary>Row 3, Column 2.</summary>
    [DataMember] public float m32;
    /// <summary>Row 3, Column 3.</summary>
    [DataMember] public float m33;
    /// <summary>Row 3, Column 4.</summary>
    [DataMember] public float m34;

    /// <summary>Row 4, Column 1 (translation X component for affine transforms).</summary>
    [DataMember] public float m41;
    /// <summary>Row 4, Column 2 (translation Y component for affine transforms).</summary>
    [DataMember] public float m42;
    /// <summary>Row 4, Column 3 (translation Z component for affine transforms).</summary>
    [DataMember] public float m43;
    /// <summary>Row 4, Column 4.</summary>
    [DataMember] public float m44;

    #endregion

    #region Construction

    /// <summary>
    /// Initializes a 4x4 matrix with explicit row-major elements.
    /// </summary>
    /// <param name="m11">Row 1, Column 1.</param>
    /// <param name="m12">Row 1, Column 2.</param>
    /// <param name="m13">Row 1, Column 3.</param>
    /// <param name="m14">Row 1, Column 4.</param>
    /// <param name="m21">Row 2, Column 1.</param>
    /// <param name="m22">Row 2, Column 2.</param>
    /// <param name="m23">Row 2, Column 3.</param>
    /// <param name="m24">Row 2, Column 4.</param>
    /// <param name="m31">Row 3, Column 1.</param>
    /// <param name="m32">Row 3, Column 2.</param>
    /// <param name="m33">Row 3, Column 3.</param>
    /// <param name="m34">Row 3, Column 4.</param>
    /// <param name="m41">Row 4, Column 1.</param>
    /// <param name="m42">Row 4, Column 2.</param>
    /// <param name="m43">Row 4, Column 3.</param>
    /// <param name="m44">Row 4, Column 4.</param>
    public Matrix(
        float m11, float m12, float m13, float m14,
        float m21, float m22, float m23, float m24,
        float m31, float m32, float m33, float m34,
        float m41, float m42, float m43, float m44)
    {
        this.m11 = m11; this.m12 = m12; this.m13 = m13; this.m14 = m14;
        this.m21 = m21; this.m22 = m22; this.m23 = m23; this.m24 = m24;
        this.m31 = m31; this.m32 = m32; this.m33 = m33; this.m34 = m34;
        this.m41 = m41; this.m42 = m42; this.m43 = m43; this.m44 = m44;
    }

    /// <summary>
    /// Gets the identity matrix.
    /// </summary>
    public static Matrix identity => new Matrix(
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1);

    #endregion

    #region Common Factories

    /// <summary>
    /// Creates a translation matrix.
    /// </summary>
    /// <param name="x">Translation on X axis.</param>
    /// <param name="y">Translation on Y axis.</param>
    /// <param name="z">Translation on Z axis.</param>
    /// <returns>A translation matrix whose translation resides in (m41, m42, m43).</returns>
    public static Matrix CreateTranslation(float x, float y, float z)
    {
        return new Matrix(
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            x, y, z, 1);
    }

    /// <summary>
    /// Creates a translation matrix.
    /// </summary>
    /// <param name="v">Translation vector.</param>
    /// <returns>A translation matrix.</returns>
    public static Matrix CreateTranslation(Vector3 v) => CreateTranslation(v.x, v.y, v.z);

    /// <summary>
    /// Creates a uniform scale matrix.
    /// </summary>
    /// <param name="scale">Uniform scale factor.</param>
    /// <returns>A scale matrix.</returns>
    public static Matrix CreateScale(float scale) => CreateScale(scale, scale, scale);

    /// <summary>
    /// Creates a non-uniform scale matrix.
    /// </summary>
    /// <param name="x">Scale factor on X axis.</param>
    /// <param name="y">Scale factor on Y axis.</param>
    /// <param name="z">Scale factor on Z axis.</param>
    /// <returns>A scale matrix.</returns>
    public static Matrix CreateScale(float x, float y, float z)
    {
        return new Matrix(
            x, 0, 0, 0,
            0, y, 0, 0,
            0, 0, z, 0,
            0, 0, 0, 1);
    }

    /// <summary>
    /// Creates a non-uniform scale matrix.
    /// </summary>
    /// <param name="v">Scale vector.</param>
    /// <returns>A scale matrix.</returns>
    public static Matrix CreateScale(Vector3 v) => CreateScale(v.x, v.y, v.z);

    /// <summary>
    /// Creates a rotation matrix around the X axis (radians).
    /// </summary>
    /// <param name="radians">Rotation angle in radians.</param>
    /// <returns>A rotation matrix.</returns>
    public static Matrix CreateRotationX(float radians)
    {
        float c = MathF.Cos(radians);
        float s = MathF.Sin(radians);
        return new Matrix(
            1, 0, 0, 0,
            0, c, s, 0,
            0, -s, c, 0,
            0, 0, 0, 1);
    }

    /// <summary>
    /// Creates a rotation matrix around the Y axis (radians).
    /// </summary>
    /// <param name="radians">Rotation angle in radians.</param>
    /// <returns>A rotation matrix.</returns>
    public static Matrix CreateRotationY(float radians)
    {
        float c = MathF.Cos(radians);
        float s = MathF.Sin(radians);
        return new Matrix(
            c, 0, -s, 0,
            0, 1, 0, 0,
            s, 0, c, 0,
            0, 0, 0, 1);
    }

    /// <summary>
    /// Creates a rotation matrix around the Z axis (radians).
    /// </summary>
    /// <param name="radians">Rotation angle in radians.</param>
    /// <returns>A rotation matrix.</returns>
    public static Matrix CreateRotationZ(float radians)
    {
        float c = MathF.Cos(radians);
        float s = MathF.Sin(radians);
        return new Matrix(
            c, s, 0, 0,
            -s, c, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1);
    }

    /// <summary>
    /// Creates a rotation matrix from a quaternion.
    /// </summary>
    /// <param name="q">Source quaternion.</param>
    /// <returns>A rotation matrix.</returns>
    public static Matrix CreateFromQuaternion(Quaternion q)
    {
        float xx = q.x * q.x, yy = q.y * q.y, zz = q.z * q.z;
        float xy = q.x * q.y, xz = q.x * q.z, yz = q.y * q.z;
        float wx = q.w * q.x, wy = q.w * q.y, wz = q.w * q.z;

        return new Matrix(
            1 - 2 * (yy + zz), 2 * (xy + wz),     2 * (xz - wy),     0,
            2 * (xy - wz),     1 - 2 * (xx + zz), 2 * (yz + wx),     0,
            2 * (xz + wy),     2 * (yz - wx),     1 - 2 * (xx + yy), 0,
            0,                 0,                 0,                 1);
    }

    #endregion

    #region Projection

    /// <summary>
    /// Creates a left-handed, perspective projection matrix ( depth range 0..1).
    /// </summary>
    /// <param name="fov">Vertical field of view in radians.</param>
    /// <param name="aspect">Viewport aspect ratio (width / height).</param>
    /// <param name="near">Near plane distance (positive).</param>
    /// <param name="far">Far plane distance (positive).</param>
    /// <returns>A perspective projection matrix.</returns>
    public static Matrix CreatePerspectiveFieldOfView(float fov, float aspect, float near, float far)
    {
        float f  = 1f / MathF.Tan(fov * 0.5f);
        float nf = 1f / (far - near);

        return new Matrix(
            f / aspect, 0, 0,                 0,
            0,          f, 0,                 0,
            0,          0, far * nf,          1,
            0,          0, -near * far * nf,  0);
    }

    /// <summary>
    /// Creates an orthographic projection matrix (depth range 0..1).
    /// </summary>
    /// <param name="width">View width.</param>
    /// <param name="height">View height.</param>
    /// <param name="near">Near plane in camera space.</param>
    /// <param name="far">Far plane in camera space.</param>
    /// <returns>An orthographic projection matrix.</returns>
    public static Matrix CreateOrthographic(float width, float height, float near, float far)
    {
        float m00 = 2f / width;
        float m11 = 2f / height;
        float m22 = 1f / (far - near);
        float m32 = -near / (far - near);

        return new Matrix(
            m00, 0,   0,   0,
            0,   m11, 0,   0,
            0,   0,   m22, 0,
            0,   0,   m32, 1
        );
    }

    #endregion

    #region View

    /// <summary>
    /// Creates a left-handed view matrix that looks from <paramref name="eye"/> to <paramref name="target"/>.
    /// </summary>
    /// <param name="eye">Camera position in world space.</param>
    /// <param name="target">Target position in world space.</param>
    /// <param name="up">Up direction in world space.</param>
    /// <returns>A view matrix.</returns>
    public static Matrix CreateLookAt(Vector3 eye, Vector3 target, Vector3 up)
    {
        // LH: forward points from eye to target
        Vector3 z = (target - eye).normalized;          // forward (+Z)
        Vector3 x = Vector3.Cross(up, z).normalized;    // right
        Vector3 y = Vector3.Cross(z, x);                // up

        return new Matrix(
            x.x, y.x, z.x, 0,
            x.y, y.y, z.y, 0,
            x.z, y.z, z.z, 0,
            -Vector3.Dot(x, eye), -Vector3.Dot(y, eye), -Vector3.Dot(z, eye), 1);
    }


    #endregion

    #region Operations

    /// <summary>
    /// Multiplies two matrices using standard matrix multiplication.
    /// </summary>
    /// <param name="a">Left operand.</param>
    /// <param name="b">Right operand.</param>
    /// <returns>The product matrix <c>a * b</c>.</returns>
    public static Matrix Multiply(Matrix a, Matrix b)
    {
        return new Matrix(
            a.m11 * b.m11 + a.m12 * b.m21 + a.m13 * b.m31 + a.m14 * b.m41,
            a.m11 * b.m12 + a.m12 * b.m22 + a.m13 * b.m32 + a.m14 * b.m42,
            a.m11 * b.m13 + a.m12 * b.m23 + a.m13 * b.m33 + a.m14 * b.m43,
            a.m11 * b.m14 + a.m12 * b.m24 + a.m13 * b.m34 + a.m14 * b.m44,

            a.m21 * b.m11 + a.m22 * b.m21 + a.m23 * b.m31 + a.m24 * b.m41,
            a.m21 * b.m12 + a.m22 * b.m22 + a.m23 * b.m32 + a.m24 * b.m42,
            a.m21 * b.m13 + a.m22 * b.m23 + a.m23 * b.m33 + a.m24 * b.m43,
            a.m21 * b.m14 + a.m22 * b.m24 + a.m23 * b.m34 + a.m24 * b.m44,

            a.m31 * b.m11 + a.m32 * b.m21 + a.m33 * b.m31 + a.m34 * b.m41,
            a.m31 * b.m12 + a.m32 * b.m22 + a.m33 * b.m32 + a.m34 * b.m42,
            a.m31 * b.m13 + a.m32 * b.m23 + a.m33 * b.m33 + a.m34 * b.m43,
            a.m31 * b.m14 + a.m32 * b.m24 + a.m33 * b.m34 + a.m34 * b.m44,

            a.m41 * b.m11 + a.m42 * b.m21 + a.m43 * b.m31 + a.m44 * b.m41,
            a.m41 * b.m12 + a.m42 * b.m22 + a.m43 * b.m32 + a.m44 * b.m42,
            a.m41 * b.m13 + a.m42 * b.m23 + a.m43 * b.m33 + a.m44 * b.m43,
            a.m41 * b.m14 + a.m42 * b.m24 + a.m43 * b.m34 + a.m44 * b.m44
        );
    }

    /// <summary>
    /// Extracts the 2D-affine portion of a matrix (XY basis + XY translation).
    /// </summary>
    /// <param name="m">Source matrix.</param>
    /// <returns>A matrix representing the 2D transform embedded in <paramref name="m"/>.</returns>
    public static Matrix Extract2DTransform(Matrix m)
    {
        return new Matrix(
            m.m11, m.m12, 0, 0,
            m.m21, m.m22, 0, 0,
            0,     0,     1, 0,
            m.m41, m.m42, 0, 1
        );
    }

    /// <summary>
    /// Returns the transpose of a matrix.
    /// </summary>
    /// <param name="a">Source matrix.</param>
    /// <returns>Transposed matrix.</returns>
    public static Matrix Transpose(Matrix a)
    {
        return new Matrix(
            a.m11, a.m21, a.m31, a.m41,
            a.m12, a.m22, a.m32, a.m42,
            a.m13, a.m23, a.m33, a.m43,
            a.m14, a.m24, a.m34, a.m44
        );
    }

    /// <summary>
    /// Returns the inverse of a matrix. If the matrix is non-invertible, returns <see cref="identity"/>.
    /// </summary>
    /// <param name="m">Source matrix.</param>
    /// <returns>Inverse matrix, or <see cref="identity"/> when not invertible.</returns>
    public static Matrix Invert(Matrix m)
    {
        float a00 = m.m11, a01 = m.m12, a02 = m.m13, a03 = m.m14;
        float a10 = m.m21, a11 = m.m22, a12 = m.m23, a13 = m.m24;
        float a20 = m.m31, a21 = m.m32, a22 = m.m33, a23 = m.m34;
        float a30 = m.m41, a31 = m.m42, a32 = m.m43, a33 = m.m44;

        float b00 = a00 * a11 - a01 * a10;
        float b01 = a00 * a12 - a02 * a10;
        float b02 = a00 * a13 - a03 * a10;
        float b03 = a01 * a12 - a02 * a11;
        float b04 = a01 * a13 - a03 * a11;
        float b05 = a02 * a13 - a03 * a12;
        float b06 = a20 * a31 - a21 * a30;
        float b07 = a20 * a32 - a22 * a30;
        float b08 = a20 * a33 - a23 * a30;
        float b09 = a21 * a32 - a22 * a31;
        float b10 = a21 * a33 - a23 * a31;
        float b11 = a22 * a33 - a23 * a32;

        float det = b00 * b11 - b01 * b10 + b02 * b09 + b03 * b08 - b04 * b07 + b05 * b06;
        if (MathF.Abs(det) < MathHelper.C_TOLERANCE)
            return identity;

        float invDet = 1f / det;

        return new Matrix(
            (a11 * b11 - a12 * b10 + a13 * b09) * invDet,
            (-a01 * b11 + a02 * b10 - a03 * b09) * invDet,
            (a31 * b05 - a32 * b04 + a33 * b03) * invDet,
            (-a21 * b05 + a22 * b04 - a23 * b03) * invDet,

            (-a10 * b11 + a12 * b08 - a13 * b07) * invDet,
            (a00 * b11 - a02 * b08 + a03 * b07) * invDet,
            (-a30 * b05 + a32 * b02 - a33 * b01) * invDet,
            (a20 * b05 - a22 * b02 + a23 * b01) * invDet,

            (a10 * b10 - a11 * b08 + a13 * b06) * invDet,
            (-a00 * b10 + a01 * b08 - a03 * b06) * invDet,
            (a30 * b04 - a31 * b02 + a33 * b00) * invDet,
            (-a20 * b04 + a21 * b02 - a23 * b00) * invDet,

            (-a10 * b09 + a11 * b07 - a12 * b06) * invDet,
            (a00 * b09 - a01 * b07 + a02 * b06) * invDet,
            (-a30 * b03 + a31 * b01 - a32 * b00) * invDet,
            (a20 * b03 - a21 * b01 + a22 * b00) * invDet
        );
    }

    #endregion

    #region Operators

    /// <summary>
    /// Multiplies two matrices.
    /// </summary>
    /// <param name="a">Left operand.</param>
    /// <param name="b">Right operand.</param>
    /// <returns>The product matrix.</returns>
    public static Matrix operator *(Matrix a, Matrix b) => Multiply(a, b);

    /// <summary>
    /// Tests matrices for approximate equality (per-element).
    /// </summary>
    /// <param name="matrix1">First matrix.</param>
    /// <param name="matrix2">Second matrix.</param>
    /// <returns><c>true</c> if all elements are approximately equal.</returns>
    public static bool operator ==(Matrix matrix1, Matrix matrix2)
    {
        return
            MathHelper.AlmostEquals(matrix1.m11, matrix2.m11) &&
            MathHelper.AlmostEquals(matrix1.m12, matrix2.m12) &&
            MathHelper.AlmostEquals(matrix1.m13, matrix2.m13) &&
            MathHelper.AlmostEquals(matrix1.m14, matrix2.m14) &&
            MathHelper.AlmostEquals(matrix1.m21, matrix2.m21) &&
            MathHelper.AlmostEquals(matrix1.m22, matrix2.m22) &&
            MathHelper.AlmostEquals(matrix1.m23, matrix2.m23) &&
            MathHelper.AlmostEquals(matrix1.m24, matrix2.m24) &&
            MathHelper.AlmostEquals(matrix1.m31, matrix2.m31) &&
            MathHelper.AlmostEquals(matrix1.m32, matrix2.m32) &&
            MathHelper.AlmostEquals(matrix1.m33, matrix2.m33) &&
            MathHelper.AlmostEquals(matrix1.m34, matrix2.m34) &&
            MathHelper.AlmostEquals(matrix1.m41, matrix2.m41) &&
            MathHelper.AlmostEquals(matrix1.m42, matrix2.m42) &&
            MathHelper.AlmostEquals(matrix1.m43, matrix2.m43) &&
            MathHelper.AlmostEquals(matrix1.m44, matrix2.m44);
    }

    /// <summary>
    /// Tests matrices for inequality.
    /// </summary>
    /// <param name="a">First matrix.</param>
    /// <param name="b">Second matrix.</param>
    /// <returns><c>true</c> if matrices are not equal.</returns>
    public static bool operator !=(Matrix a, Matrix b) => !(a == b);

    /// <summary>
    /// Converts to <see cref="System.Numerics.Matrix4x4"/> preserving row-major element order.
    /// </summary>
    /// <param name="m">Source matrix.</param>
    public static implicit operator System.Numerics.Matrix4x4(Matrix m)
    {
        return new System.Numerics.Matrix4x4(
            m.m11, m.m12, m.m13, m.m14,
            m.m21, m.m22, m.m23, m.m24,
            m.m31, m.m32, m.m33, m.m34,
            m.m41, m.m42, m.m43, m.m44
        );
    }

    /// <summary>
    /// Converts from <see cref="System.Numerics.Matrix4x4"/> preserving row-major element order.
    /// </summary>
    /// <param name="m">Source matrix.</param>
    public static implicit operator Matrix(System.Numerics.Matrix4x4 m)
    {
        return new Matrix
        {
            m11 = m.M11, m12 = m.M12, m13 = m.M13, m14 = m.M14,
            m21 = m.M21, m22 = m.M22, m23 = m.M23, m24 = m.M24,
            m31 = m.M31, m32 = m.M32, m33 = m.M33, m34 = m.M34,
            m41 = m.M41, m42 = m.M42, m43 = m.M43, m44 = m.M44
        };
    }

    #endregion

    #region Equality / Formatting

    /// <summary>
    /// Determines whether this instance is equal to another matrix.
    /// </summary>
    /// <param name="other">Other matrix.</param>
    /// <returns><c>true</c> if equal.</returns>
    public bool Equals(Matrix other) => this == other;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Matrix other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return this.m11.GetHashCode() + this.m12.GetHashCode() + this.m13.GetHashCode() + this.m14.GetHashCode() +
               this.m21.GetHashCode() + this.m22.GetHashCode() + this.m23.GetHashCode() + this.m24.GetHashCode() +
               this.m31.GetHashCode() + this.m32.GetHashCode() + this.m33.GetHashCode() + this.m34.GetHashCode() +
               this.m41.GetHashCode() + this.m42.GetHashCode() + this.m43.GetHashCode() + this.m44.GetHashCode();
    }

    /// <summary>
    /// Returns a multi-line string representation of the matrix in row-major order.
    /// </summary>

    public override string ToString()
    {
        return $"[{m11}, {m12}, {m13}, {m14}]\n" +
               $"[{m21}, {m22}, {m23}, {m24}]\n" +
               $"[{m31}, {m32}, {m33}, {m34}]\n" +
               $"[{m41}, {m42}, {m43}, {m44}]";
    }
    
    #endregion
}
