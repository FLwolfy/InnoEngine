using System;

using Inno.Runtime;
using Inno.Runtime.Contracts;

namespace Inno.UI.Runtime;

/// <summary>
/// Creates one UI service for every isolated runtime session.
/// </summary>
public sealed class UiRuntimeFactory : IRuntimeSubsystemFactory
{
    private readonly Func<RuntimeSubsystemContext, UiRuntime> m_runtimeFactory;

    /// <summary>
    /// Creates a reusable UI runtime factory.
    /// </summary>
    /// <param name="runtimeFactory">
    /// The callback that creates one runtime per session.
    /// </param>
    public UiRuntimeFactory(Func<RuntimeSubsystemContext, UiRuntime> runtimeFactory)
    {
        m_runtimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
    }

    /// <summary>
    /// Gets stable ordering metadata that makes UI available before scene simulation.
    /// </summary>
    public RuntimeSubsystemDescriptor descriptor { get; } = new(
        new RuntimeSubsystemId("inno.runtime.ui"),
        order: -700,
        dependencies:
        [
            new RuntimeSubsystemId("inno.runtime.input")
        ]);

    /// <summary>
    /// Creates one session-owned UI runtime.
    /// </summary>
    /// <param name="context">
    /// The isolated session construction context.
    /// </param>
    /// <returns>
    /// The unattached runtime.
    /// </returns>
    public IRuntimeSubsystem Create(RuntimeSubsystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return m_runtimeFactory(context)
            ?? throw new InvalidOperationException("The UI runtime factory returned null.");
    }
}
