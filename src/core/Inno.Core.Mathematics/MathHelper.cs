#nullable disable

using System;
using System.Runtime.CompilerServices;

namespace Inno.Core.Mathematics;

/// <summary>
/// Contains commonly used precalculated values and mathematical operations.
/// </summary>
public static class MathHelper
{
  public const float C_TOLERANCE = 1e-6f;
  
  /// <summary>
  /// Determines whether two floating-point numbers are approximately equal,
  /// using a relative tolerance based on the magnitude of the numbers.
  /// This is useful for comparing floats where rounding errors may occur.
  /// </summary>
  /// <param name="a">The first float value to compare.</param>
  /// <param name="b">The second float value to compare.</param>
  /// <param name="relTolerance">
  /// The relative tolerance allowed between <paramref name="a"/> and <paramref name="b"/>.
  /// Default is 1e-6. The comparison checks if the difference is within
  /// <c>relTolerance * max(|a|, |b|)</c>.
  /// </param>
  /// <returns>
  /// <c>true</c> if the absolute difference between <paramref name="a"/> and <paramref name="b"/>
  /// is less than or equal to the relative tolerance multiplied by the larger magnitude of the two values;
  /// otherwise, <c>false</c>.
  /// </returns>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static bool AlmostEquals(float a, float b, float relTolerance = C_TOLERANCE)
  {
    return MathF.Abs(a - b) <= relTolerance * MathF.Max(MathF.Abs(a), MathF.Abs(b));
  }
  
  /// <summary>
  /// Returns the Cartesian coordinate for one axis of a point that is defined by a given triangle and two normalized barycentric (areal) coordinates.
  /// </summary>
  /// <param name="value1">The coordinate on one axis of vertex 1 of the defining triangle.</param>
  /// <param name="value2">The coordinate on the same axis of vertex 2 of the defining triangle.</param>
  /// <param name="value3">The coordinate on the same axis of vertex 3 of the defining triangle.</param>
  /// <param name="amount1">The normalized barycentric (areal) coordinate b2, equal to the weighting factor for vertex 2, the coordinate of which is specified in value2.</param>
  /// <param name="amount2">The normalized barycentric (areal) coordinate b3, equal to the weighting factor for vertex 3, the coordinate of which is specified in value3.</param>
  /// <returns>Cartesian coordinate of the specified point with respect to the axis being used.</returns>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static float Barycentric(
    float value1,
    float value2,
    float value3,
    float amount1,
    float amount2)
  {
    return (float) ((double) value1 + ((double) value2 - (double) value1) * (double) amount1 + ((double) value3 - (double) value1) * (double) amount2);
  }

  /// <summary>
  /// Performs a Catmull-Rom interpolation using the specified positions.
  /// </summary>
  /// <param name="value1">The first position in the interpolation.</param>
  /// <param name="value2">The second position in the interpolation.</param>
  /// <param name="value3">The third position in the interpolation.</param>
  /// <param name="value4">The fourth position in the interpolation.</param>
  /// <param name="amount">Weighting factor.</param>
  /// <returns>A position that is the result of the Catmull-Rom interpolation.</returns>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static float CatmullRom(
    float value1,
    float value2,
    float value3,
    float value4,
    float amount)
  {
    double num1 = (double) amount * (double) amount;
    double num2 = num1 * (double) amount;
    return (float) (0.5 * (2.0 * (double) value2 + ((double) value3 - (double) value1) * (double) amount + (2.0 * (double) value1 - 5.0 * (double) value2 + 4.0 * (double) value3 - (double) value4) * num1 + (3.0 * (double) value2 - (double) value1 - 3.0 * (double) value3 + (double) value4) * num2));
  }

  /// <summary>
  /// Calculates the absolute value of the difference of two values.
  /// </summary>
  /// <param name="value1">Source value.</param>
  /// <param name="value2">Source value.</param>
  /// <returns>Distance between the two values.</returns>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static float Distance(float value1, float value2) => MathF.Abs(value1 - value2);

  /// <summary>
  /// Performs a Hermite spline interpolation.
  /// </summary>
  /// <param name="value1">Source position.</param>
  /// <param name="tangent1">Source tangent.</param>
  /// <param name="value2">Source position.</param>
  /// <param name="tangent2">Source tangent.</param>
  /// <param name="amount">Weighting factor.</param>
  /// <returns>The result of the Hermite spline interpolation.</returns>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static float Hermite(
    float value1,
    float tangent1,
    float value2,
    float tangent2,
    float amount)
  {
    double num1 = (double) value1;
    double num2 = (double) value2;
    double num3 = (double) tangent1;
    double num4 = (double) tangent2;
    double num5 = (double) amount;
    double num6 = num5 * num5 * num5;
    double num7 = num5 * num5;
    return (double) amount != 0.0 ? ((double) amount != 1.0 ? (float) ((2.0 * num1 - 2.0 * num2 + num4 + num3) * num6 + (3.0 * num2 - 3.0 * num1 - 2.0 * num3 - num4) * num7 + num3 * num5 + num1) : value2) : value1;
  }

  /// <summary>Linearly interpolates between two values.</summary>
  /// <param name="value1">Source value.</param>
  /// <param name="value2">Destination value.</param>
  /// <param name="amount">Value between 0 and 1 indicating the weight of value2.</param>
  /// <returns>Interpolated value.</returns>
  /// <remarks>This method performs the linear interpolation based on the following formula:
  /// <code>value1 + (value2 - value1) * amount</code>
  /// Passing amount a value of 0 will cause value1 to be returned, a value of 1 will cause value2 to be returned.
  /// See <see cref="M:MathHelper.LerpPrecise(System.Single,System.Single,System.Single)" /> for a less efficient version with more precision around edge cases.
  /// </remarks>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static float Lerp(float value1, float value2, float amount)
  {
    return value1 + (value2 - value1) * amount;
  }

  /// <summary>Linearly interpolates between two values without clamping the amount.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static float LerpUnclamped(float value1, float value2, float amount)
  {
    return value1 + (value2 - value1) * amount;
  }

  /// <summary>
  /// Linearly interpolates between two values.
  /// This method is a less efficient, more precise version of <see cref="M:MathHelper.Lerp(System.Single,System.Single,System.Single)" />.
  /// See remarks for more info.
  /// </summary>
  /// <param name="value1">Source value.</param>
  /// <param name="value2">Destination value.</param>
  /// <param name="amount">Value between 0 and 1 indicating the weight of value2.</param>
  /// <returns>Interpolated value.</returns>
  /// <remarks>This method performs the linear interpolation based on the following formula:
  /// <code>((1 - amount) * value1) + (value2 * amount)</code>.
  /// Passing amount a value of 0 will cause value1 to be returned, a value of 1 will cause value2 to be returned.
  /// This method does not have the floating point precision issue that <see cref="M:MathHelper.Lerp(System.Single,System.Single,System.Single)" /> has.
  /// i.e. If there is a big gap between value1 and value2 in magnitude (e.g. value1=10000000000000000, value2=1),
  /// right at the edge of the interpolation range (amount=1), <see cref="M:MathHelper.Lerp(System.Single,System.Single,System.Single)" /> will return 0 (whereas it should return 1).
  /// This also holds for value1=10^17, value2=10; value1=10^18,value2=10^2... so on.
  /// For an in depth explanation of the issue, see below references:
  /// Relevant Wikipedia Article: https://en.wikipedia.org/wiki/Linear_interpolation#Programming_language_support
  /// Relevant StackOverflow Answer: http://stackoverflow.com/questions/4353525/floating-point-linear-interpolation#answer-23716956
  /// </remarks>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static float LerpPrecise(float value1, float value2, float amount)
  {
    return (float) ((1.0 - (double) amount) * (double) value1 + (double) value2 * (double) amount);
  }

  /// <summary>
  /// Interpolates between two values using a cubic equation.
  /// </summary>
  /// <param name="value1">Source value.</param>
  /// <param name="value2">Source value.</param>
  /// <param name="amount">Weighting value.</param>
  /// <returns>Interpolated value.</returns>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static float SmoothStep(float value1, float value2, float amount)
  {
    float amount1 = Math.Clamp(amount, 0.0f, 1f);
    return Hermite(value1, 0.0f, value2, 0.0f, amount1);
  }

  /// <summary>
  /// Converts radians to degrees.
  /// </summary>
  /// <param name="radians">The angle in radians.</param>
  /// <returns>The angle in degrees.</returns>
  /// <remarks>
  /// This method uses double precision internally,
  /// though it returns single float
  /// Factor = 180 / pi
  /// </remarks>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static float ToDegrees(float radians) => radians * 57.29578f;

  /// <summary>
  /// Converts degrees to radians.
  /// </summary>
  /// <param name="degrees">The angle in degrees.</param>
  /// <returns>The angle in radians.</returns>
  /// <remarks>
  /// This method uses double precision internally,
  /// though it returns single float
  /// Factor = pi / 180
  /// </remarks>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static float ToRadians(float degrees) => degrees * ((float) MathF.PI / 180f);

  /// <summary>Clamps a value to the inclusive range [min, max].</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);

  /// <summary>Clamps a value to the inclusive range [0, 1].</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static float Saturate(float value) => Math.Clamp(value, 0f, 1f);

  /// <summary>Returns true if the value is finite (not NaN or infinity).</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

  /// <summary>
  /// Reduces a given angle to a value between π and -π.
  /// </summary>
  /// <param name="angle">The angle to reduce, in radians.</param>
  /// <returns>The new angle, in radians.</returns>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static float WrapAngle(float angle)
  {
    if ((double) angle > -3.1415927410125732 && (double) angle <= 3.1415927410125732)
      return angle;
    angle %= 6.2831855f;
    if ((double) angle <= -3.1415927410125732)
      return angle + 6.2831855f;
    return (double) angle > 3.1415927410125732 ? angle - 6.2831855f : angle;
  }

  /// <summary>
  /// Determines if value is powered by two.
  /// </summary>
  /// <param name="value">A value.</param>
  /// <returns><c>true</c> if <c>value</c> is powered by two; otherwise <c>false</c>.</returns>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static bool IsPowerOfTwo(int value) => value > 0 && (value & value - 1) == 0;
}
