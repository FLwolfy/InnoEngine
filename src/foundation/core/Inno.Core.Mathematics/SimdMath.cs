using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Inno.Core.Mathematics;

internal static class SimdMath
{
    /// <summary>
    /// Calculates a four-component scalar dot product.
    /// </summary>
    /// <param name="a">
    /// The first operand or interpolation endpoint.
    /// </param>
    /// <param name="b">
    /// The second operand or interpolation endpoint.
    /// </param>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot4(Vector128<float> a, Vector128<float> b)
    {
        if (Sse.IsSupported)
        {
            var mul = Sse.Multiply(a, b);
            return mul.GetElement(0) + mul.GetElement(1) + mul.GetElement(2) + mul.GetElement(3);
        }

        if (AdvSimd.IsSupported)
        {
            var mul = AdvSimd.Multiply(a, b);
            return mul.GetElement(0) + mul.GetElement(1) + mul.GetElement(2) + mul.GetElement(3);
        }

        return a.GetElement(0) * b.GetElement(0)
            + a.GetElement(1) * b.GetElement(1)
            + a.GetElement(2) * b.GetElement(2)
            + a.GetElement(3) * b.GetElement(3);
    }

    /// <summary>
    /// Calculates a two-component scalar dot product.
    /// </summary>
    /// <param name="ax">
    /// The ax consumed by dot2; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="ay">
    /// The ay consumed by dot2; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="bx">
    /// The bx consumed by dot2; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="by">
    /// The by consumed by dot2; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot2(float ax, float ay, float bx, float by)
    {
        if (Sse.IsSupported)
        {
            var a = Vector128.Create(ax, ay, 0f, 0f);
            var b = Vector128.Create(bx, by, 0f, 0f);
            var mul = Sse.Multiply(a, b);
            return mul.GetElement(0) + mul.GetElement(1);
        }

        if (AdvSimd.IsSupported)
        {
            var a = Vector128.Create(ax, ay, 0f, 0f);
            var b = Vector128.Create(bx, by, 0f, 0f);
            var mul = AdvSimd.Multiply(a, b);
            return mul.GetElement(0) + mul.GetElement(1);
        }

        return (ax * bx) + (ay * by);
    }

    /// <summary>
    /// Calculates a three-component scalar dot product.
    /// </summary>
    /// <param name="ax">
    /// The ax consumed by dot3; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="ay">
    /// The ay consumed by dot3; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="az">
    /// The az consumed by dot3; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="bx">
    /// The bx consumed by dot3; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="by">
    /// The by consumed by dot3; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="bz">
    /// The bz consumed by dot3; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot3(float ax, float ay, float az, float bx, float by, float bz)
    {
        if (Sse.IsSupported)
        {
            var a = Vector128.Create(ax, ay, az, 0f);
            var b = Vector128.Create(bx, by, bz, 0f);
            var mul = Sse.Multiply(a, b);
            return mul.GetElement(0) + mul.GetElement(1) + mul.GetElement(2);
        }

        if (AdvSimd.IsSupported)
        {
            var a = Vector128.Create(ax, ay, az, 0f);
            var b = Vector128.Create(bx, by, bz, 0f);
            var mul = AdvSimd.Multiply(a, b);
            return mul.GetElement(0) + mul.GetElement(1) + mul.GetElement(2);
        }

        return (ax * bx) + (ay * by) + (az * bz);
    }

    /// <summary>
    /// Calculates a four-component scalar dot product.
    /// </summary>
    /// <param name="ax">
    /// The ax consumed by dot4; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="ay">
    /// The ay consumed by dot4; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="az">
    /// The az consumed by dot4; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="aw">
    /// The aw consumed by dot4; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="bx">
    /// The bx consumed by dot4; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="by">
    /// The by consumed by dot4; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="bz">
    /// The bz consumed by dot4; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="bw">
    /// The bw consumed by dot4; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot4(float ax, float ay, float az, float aw, float bx, float by, float bz, float bw)
    {
        if (Sse.IsSupported)
        {
            var a = Vector128.Create(ax, ay, az, aw);
            var b = Vector128.Create(bx, by, bz, bw);
            var mul = Sse.Multiply(a, b);
            return mul.GetElement(0) + mul.GetElement(1) + mul.GetElement(2) + mul.GetElement(3);
        }

        if (AdvSimd.IsSupported)
        {
            var a = Vector128.Create(ax, ay, az, aw);
            var b = Vector128.Create(bx, by, bz, bw);
            var mul = AdvSimd.Multiply(a, b);
            return mul.GetElement(0) + mul.GetElement(1) + mul.GetElement(2) + mul.GetElement(3);
        }

        return (ax * bx) + (ay * by) + (az * bz) + (aw * bw);
    }
}
