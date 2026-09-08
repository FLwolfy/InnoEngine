using Inno.Runtime.Contracts;
using System;

using Inno.Runtime;

namespace Inno.Animation.Runtime;

/// <summary>
/// Creates one animation runtime feature for each isolated session.
/// </summary>
public sealed class AnimationRuntimeFactory : IRuntimeSubsystemFactory
{
    private readonly Func<RuntimeSubsystemContext, AnimationRuntime> m_runtimeFactory;

    /// <summary>
    /// Creates a reusable feature factory around a composition-owned runtime callback.
    /// </summary>
    /// <param name="runtimeFactory">
    /// Callback that creates a distinct animation runtime for each session.
    /// </param>
    public AnimationRuntimeFactory(
        Func<RuntimeSubsystemContext, AnimationRuntime> runtimeFactory)
    {
        m_runtimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
    }

    /// <summary>
    /// Gets ordering metadata that evaluates animation after scene simulation.
    /// </summary>
    public RuntimeSubsystemDescriptor descriptor { get; } = new(
        new RuntimeSubsystemId("inno.runtime.animation"),
        order: 400,
        dependencies: [new RuntimeSubsystemId("inno.runtime.scene")]);

    /// <summary>
    /// Creates a feature over a newly allocated animation runtime.
    /// </summary>
    /// <param name="context">
    /// Isolated session context supplied to the runtime factory.
    /// </param>
    /// <returns>
    /// A new session-owned animation feature.
    /// </returns>
    public IRuntimeSubsystem Create(RuntimeSubsystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        AnimationRuntime runtime = m_runtimeFactory(context)
            ?? throw new InvalidOperationException("The animation runtime factory returned null.");
        return runtime;
    }
}
