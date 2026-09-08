using Inno.Runtime.Contracts;
using System;

using Inno.Runtime;

namespace Inno.Audio.Runtime;

/// <summary>
/// Creates one audio lifecycle feature for every isolated runtime session.
/// </summary>
public sealed class AudioRuntimeFactory : IRuntimeSubsystemFactory
{
    private readonly Func<RuntimeSubsystemContext, AudioRuntime> m_runtimeFactory;

    /// <summary>
    /// Creates a reusable factory around a composition-owned audio runtime callback.
    /// </summary>
    /// <param name="runtimeFactory">
    /// The callback that creates a distinct audio runtime for the supplied session.
    /// </param>
    public AudioRuntimeFactory(Func<RuntimeSubsystemContext, AudioRuntime> runtimeFactory)
    {
        m_runtimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
    }

    /// <summary>
    /// Gets stable ordering metadata that updates audio after scene simulation.
    /// </summary>
    public RuntimeSubsystemDescriptor descriptor { get; } = new(
        new RuntimeSubsystemId("inno.runtime.audio"),
        order: 500,
        dependencies: [new RuntimeSubsystemId("inno.runtime.scene")]);

    /// <summary>
    /// Creates an audio feature over a newly allocated runtime layer.
    /// </summary>
    /// <param name="context">
    /// The isolated session context passed to the audio runtime factory.
    /// </param>
    /// <returns>
    /// A new feature that exclusively owns its audio runtime layer.
    /// </returns>
    public IRuntimeSubsystem Create(RuntimeSubsystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        AudioRuntime runtime = m_runtimeFactory(context)
            ?? throw new InvalidOperationException("The audio runtime factory returned null.");
        return runtime;
    }
}
