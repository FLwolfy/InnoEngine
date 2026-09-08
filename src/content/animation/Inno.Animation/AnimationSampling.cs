namespace Inno.Animation;

/// <summary>
/// Provides backend-neutral interpolation and weighted composition used by animation runtimes.
/// </summary>
public static class AnimationSampling
{
    /// <summary>
    /// Interpolates compatible values, normalizing quaternion output.
    /// </summary>
    /// <param name="left">
    /// The initial value.
    /// </param>
    /// <param name="right">
    /// The final value of the same kind.
    /// </param>
    /// <param name="amount">
    /// The interpolation fraction, clamped to zero through one.
    /// </param>
    /// <returns>
    /// The interpolated value.
    /// </returns>
    public static AnimationValue Interpolate(AnimationValue left, AnimationValue right, float amount)
        => AnimationValue.Lerp(left, right, amount);

    /// <summary>
    /// Adds one weighted value to an accumulation of the same kind.
    /// </summary>
    /// <param name="sum">
    /// The current weighted sum.
    /// </param>
    /// <param name="value">
    /// The value to accumulate.
    /// </param>
    /// <param name="weight">
    /// The blend weight.
    /// </param>
    /// <returns>
    /// The updated sum.
    /// </returns>
    public static AnimationValue AddWeighted(AnimationValue sum, AnimationValue value, float weight)
        => AnimationValue.AddWeighted(sum, value, weight);

    /// <summary>
    /// Normalizes a weighted sum, including quaternion normalization.
    /// </summary>
    /// <param name="sum">
    /// The accumulated sum.
    /// </param>
    /// <param name="inverseWeight">
    /// The reciprocal total weight.
    /// </param>
    /// <returns>
    /// The final blended value.
    /// </returns>
    public static AnimationValue CompleteWeighted(AnimationValue sum, float inverseWeight)
        => AnimationValue.WeightedAverage(sum, inverseWeight);
}
