using System;

using Inno.Core.Serialization;

namespace Inno.Animation;

/// <summary>
/// Identifies one backend-neutral value destination inside an animation binding scope.
/// </summary>
public readonly record struct AnimationBindingId
{
    /// <summary>
    /// Creates a stable animation binding identifier.
    /// </summary>
    /// <param name="value">
    /// The non-empty binding protocol value.
    /// </param>
    public AnimationBindingId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>
    /// Gets the stable binding protocol value.
    /// </summary>
    public string value { get; }

    /// <summary>
    /// Gets whether this identifier contains a usable value.
    /// </summary>
    public bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Formats this identifier for diagnostics and persistence.
    /// </summary>
    /// <returns>
    /// The protocol value, or an empty string for an uninitialized identifier.
    /// </returns>
    public override string ToString() => value ?? string.Empty;
}

/// <summary>
/// Describes the number and interpretation of components in an animation value.
/// </summary>
public enum AnimationValueKind
{
    /// <summary>
    /// A single scalar component.
    /// </summary>
    Scalar,

    /// <summary>
    /// Two independent components.
    /// </summary>
    Vector2,

    /// <summary>
    /// Three independent components.
    /// </summary>
    Vector3,

    /// <summary>
    /// Four independent components.
    /// </summary>
    Vector4,

    /// <summary>
    /// Four normalized rotation components.
    /// </summary>
    Quaternion
}

/// <summary>
/// Stores one backend-neutral scalar, vector, or quaternion animation value.
/// </summary>
public struct AnimationValue
{
    /// <summary>
    /// Creates a four-component animation value.
    /// </summary>
    /// <param name="kind">
    /// Interpretation of the populated components.
    /// </param>
    /// <param name="x">
    /// First component.
    /// </param>
    /// <param name="y">
    /// Second component.
    /// </param>
    /// <param name="z">
    /// Third component.
    /// </param>
    /// <param name="w">
    /// Fourth component.
    /// </param>
    public AnimationValue(AnimationValueKind kind, float x, float y = 0f, float z = 0f, float w = 0f)
    {
        this.kind = kind;
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }

    /// <summary>
    /// Gets or sets the component interpretation.
    /// </summary>
    [SerializableProperty]
    public AnimationValueKind kind { get; set; }

    /// <summary>
    /// Gets or sets the first component.
    /// </summary>
    [SerializableProperty]
    public float x { get; set; }

    /// <summary>
    /// Gets or sets the second component.
    /// </summary>
    [SerializableProperty]
    public float y { get; set; }

    /// <summary>
    /// Gets or sets the third component.
    /// </summary>
    [SerializableProperty]
    public float z { get; set; }

    /// <summary>
    /// Gets or sets the fourth component.
    /// </summary>
    [SerializableProperty]
    public float w { get; set; }

    internal static AnimationValue Lerp(AnimationValue left, AnimationValue right, float amount)
    {
        if (left.kind != right.kind)
            throw new InvalidOperationException("Animation values with different kinds cannot be interpolated.");
        float t = Math.Clamp(amount, 0f, 1f);
        var value = new AnimationValue(
            left.kind,
            left.x + ((right.x - left.x) * t),
            left.y + ((right.y - left.y) * t),
            left.z + ((right.z - left.z) * t),
            left.w + ((right.w - left.w) * t));
        return left.kind == AnimationValueKind.Quaternion ? NormalizeQuaternion(value) : value;
    }

    internal static AnimationValue WeightedAverage(AnimationValue sum, float inverseWeight)
    {
        var value = new AnimationValue(
            sum.kind,
            sum.x * inverseWeight,
            sum.y * inverseWeight,
            sum.z * inverseWeight,
            sum.w * inverseWeight);
        return sum.kind == AnimationValueKind.Quaternion ? NormalizeQuaternion(value) : value;
    }

    internal static AnimationValue AddWeighted(AnimationValue sum, AnimationValue value, float weight)
    {
        if (sum.kind != value.kind)
            throw new InvalidOperationException("Animation tracks targeting one binding must use one value kind.");
        sum.x += value.x * weight;
        sum.y += value.y * weight;
        sum.z += value.z * weight;
        sum.w += value.w * weight;
        return sum;
    }

    private static AnimationValue NormalizeQuaternion(AnimationValue value)
    {
        float length = MathF.Sqrt(
            (value.x * value.x) +
            (value.y * value.y) +
            (value.z * value.z) +
            (value.w * value.w));
        return length <= float.Epsilon
            ? new AnimationValue(AnimationValueKind.Quaternion, 0f, 0f, 0f, 1f)
            : new AnimationValue(
                AnimationValueKind.Quaternion,
                value.x / length,
                value.y / length,
                value.z / length,
                value.w / length);
    }
}

/// <summary>
/// Selects how values are sampled between adjacent keyframes.
/// </summary>
public enum AnimationInterpolation
{
    /// <summary>
    /// Holds the previous keyframe value until the next keyframe.
    /// </summary>
    Step,

    /// <summary>
    /// Blends linearly between adjacent keyframes.
    /// </summary>
    Linear
}

/// <summary>
/// Selects the session clock used to advance an animation playback.
/// </summary>
public enum AnimationClock
{
    /// <summary>
    /// Uses scaled simulation time and stops while the session is paused.
    /// </summary>
    Scaled,

    /// <summary>
    /// Uses unscaled session time and continues while simulation is paused.
    /// </summary>
    Unscaled
}

/// <summary>
/// Describes the observable lifecycle of one animation playback.
/// </summary>
public enum AnimationPlaybackState
{
    /// <summary>
    /// The playback is advancing and producing samples.
    /// </summary>
    Playing,

    /// <summary>
    /// The playback retains its position without advancing.
    /// </summary>
    Paused
}

/// <summary>
/// Stores immutable options used to begin one animation playback.
/// </summary>
public struct AnimationPlayOptions
{
    /// <summary>
    /// Gets the default playback options.
    /// </summary>
    public static AnimationPlayOptions defaultValue => new()
    {
        speed = 1f,
        weight = 1f,
        layer = 0,
        clock = AnimationClock.Scaled
    };

    /// <summary>
    /// Gets or sets the non-negative playback speed multiplier.
    /// </summary>
    public float speed { get; set; }

    /// <summary>
    /// Gets or sets the finite blend weight in the inclusive zero-to-one range.
    /// </summary>
    public float weight { get; set; }

    /// <summary>
    /// Gets or sets the deterministic blend layer.
    /// </summary>
    public int layer { get; set; }

    /// <summary>
    /// Gets or sets whether the clip repeats after its duration.
    /// </summary>
    public bool loop { get; set; }

    /// <summary>
    /// Gets or sets the session clock used by the playback.
    /// </summary>
    public AnimationClock clock { get; set; }
}

/// <summary>
/// Identifies one generation-checked animation playback owned by a runtime service.
/// </summary>
public readonly record struct AnimationPlaybackHandle
{
    internal AnimationPlaybackHandle(ulong value, uint runtimeGeneration)
    {
        this.value = value;
        this.runtimeGeneration = runtimeGeneration;
    }

    /// <summary>
    /// Gets whether the handle was initialized by an animation service.
    /// </summary>
    public bool isValid => value != 0 && runtimeGeneration != 0;

    internal ulong value { get; }

    /// <summary>
    /// Gets the isolated runtime generation that created this handle.
    /// </summary>
    public uint runtimeGeneration { get; }
}

/// <summary>
/// Stores one final blended value emitted for a stable binding identifier.
/// </summary>
public readonly record struct AnimationSample
{
    /// <summary>
    /// Creates one final animation sample.
    /// </summary>
    /// <param name="binding">
    /// Stable target binding.
    /// </param>
    /// <param name="value">
    /// Final blended value.
    /// </param>
    /// <param name="target">
    /// The weak destination or explicit sampling-only policy.
    /// </param>
    public AnimationSample(AnimationTarget target, AnimationBindingId binding, AnimationValue value)
    {
        if (!target.isValid)
            throw new ArgumentException("A valid animation destination is required.", nameof(target));
        if (!binding.isValid)
            throw new ArgumentException("A valid animation binding is required.", nameof(binding));
        this.binding = binding;
        this.target = target;
        this.value = value;
    }

    /// <summary>
    /// Gets the stable target binding.
    /// </summary>
    public AnimationBindingId binding { get; }

    /// <summary>
    /// Gets the generation-qualified destination for this sample.
    /// </summary>
    public AnimationTarget target { get; }

    /// <summary>
    /// Gets the final blended value.
    /// </summary>
    public AnimationValue value { get; }
}

/// <summary>
/// Receives final backend-neutral samples after runtime blending has completed.
/// </summary>
public interface IAnimationBindingSink
{
    /// <summary>
    /// Applies one immutable frame of final animation samples.
    /// </summary>
    /// <param name="samples">
    /// Samples sorted by stable binding identifier.
    /// </param>
    void Apply(ReadOnlySpan<AnimationSample> samples);
}
