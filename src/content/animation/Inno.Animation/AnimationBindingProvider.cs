using System;

namespace Inno.Animation;

/// <summary>
/// Applies one declared binding protocol on the owner thread; implementations never run in native callbacks.
/// </summary>
public abstract class AnimationBindingProvider : IDisposable
{
    /// <summary>
    /// Attempts to apply a value to a destination resolved through Identity.
    /// </summary>
    /// <param name="target">
    /// The weak, generation-qualified destination.
    /// </param>
    /// <param name="value">
    /// The final blended value for this provider's declared protocol.
    /// </param>
    /// <returns>
    /// True when applied; false when the destination is currently missing or incompatible.
    /// </returns>
    public abstract bool TryApply(AnimationTarget target, AnimationValue value);

    /// <summary>
    /// Releases subscriptions and resources owned by this extension generation.
    /// </summary>
    public virtual void Dispose() { }
}
