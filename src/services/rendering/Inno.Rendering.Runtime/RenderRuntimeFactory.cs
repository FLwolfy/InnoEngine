using Inno.Runtime.Contracts;
using System;

using Inno.Runtime;

namespace Inno.Rendering.Runtime;

/// <summary>
/// Creates one rendering lifecycle feature for every isolated runtime session.
/// </summary>
public sealed class RenderRuntimeFactory : IRuntimeSubsystemFactory
{
    private readonly Func<RuntimeSubsystemContext, RenderRuntime> m_runtimeFactory;

    /// <summary>
    /// Creates a reusable factory around a composition-owned rendering runtime callback.
    /// </summary>
    /// <param name="runtimeFactory">
    /// The callback that creates a distinct rendering runtime for the supplied session.
    /// </param>
    public RenderRuntimeFactory(Func<RuntimeSubsystemContext, RenderRuntime> runtimeFactory)
    {
        m_runtimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
    }

    /// <summary>
    /// Gets stable ordering metadata that places rendering after simulation features.
    /// </summary>
    public RuntimeSubsystemDescriptor descriptor { get; } = new(
        new RuntimeSubsystemId("inno.runtime.rendering"),
        order: 1000,
        lifetime: RuntimeSubsystemLifetime.Host);

    /// <summary>
    /// Creates a rendering feature over a newly allocated runtime layer.
    /// </summary>
    /// <param name="context">
    /// The isolated session context passed to the rendering runtime factory.
    /// </param>
    /// <returns>
    /// A new feature that exclusively owns its rendering runtime layer.
    /// </returns>
    public IRuntimeSubsystem Create(RuntimeSubsystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RenderRuntime runtime = m_runtimeFactory(context)
            ?? throw new InvalidOperationException("The rendering runtime factory returned null.");
        return runtime;
    }
}
