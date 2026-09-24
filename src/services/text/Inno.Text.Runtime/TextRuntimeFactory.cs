using System;

using Inno.Runtime;
using Inno.Runtime.Contracts;

namespace Inno.Text.Runtime;

/// <summary>
/// Creates one text service for every isolated runtime session.
/// </summary>
public sealed class TextRuntimeFactory : IRuntimeSubsystemFactory
{
    private readonly Func<RuntimeSubsystemContext, TextRuntime> m_runtimeFactory;

    /// <summary>
    /// Creates a reusable text runtime factory.
    /// </summary>
    /// <param name="runtimeFactory">The callback that creates one runtime per session.</param>
    public TextRuntimeFactory(Func<RuntimeSubsystemContext, TextRuntime> runtimeFactory)
    {
        m_runtimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
    }

    /// <summary>
    /// Gets stable ordering metadata that makes text available before scene simulation.
    /// </summary>
    public RuntimeSubsystemDescriptor descriptor { get; } = new(
        new RuntimeSubsystemId("inno.runtime.text"),
        order: -800);

    /// <summary>
    /// Creates one session-owned text runtime.
    /// </summary>
    /// <param name="context">The isolated session construction context.</param>
    /// <returns>The unattached runtime.</returns>
    public IRuntimeSubsystem Create(RuntimeSubsystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return m_runtimeFactory(context)
            ?? throw new InvalidOperationException("The text runtime factory returned null.");
    }
}
