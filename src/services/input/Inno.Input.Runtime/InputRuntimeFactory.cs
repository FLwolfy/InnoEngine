using Inno.Runtime.Contracts;
using System;

using Inno.Runtime;

namespace Inno.Input.Runtime;

/// <summary>
/// Creates one frame-scoped input service over a caller-selected platform backend.
/// </summary>
public sealed class InputRuntimeFactory : IRuntimeSubsystemFactory
{
    private readonly Func<RuntimeSubsystemContext, IInputBackend> m_backendFactory;

    /// <summary>
    /// Creates a reusable factory that obtains an exclusively owned backend for each session.
    /// </summary>
    /// <param name="backendFactory">
    /// The composition callback that creates a distinct backend for the supplied session context.
    /// </param>
    public InputRuntimeFactory(Func<RuntimeSubsystemContext, IInputBackend> backendFactory)
    {
        m_backendFactory = backendFactory ?? throw new ArgumentNullException(nameof(backendFactory));
    }

    /// <summary>
    /// Gets stable ordering metadata for the frame-scoped input service.
    /// </summary>
    public RuntimeSubsystemDescriptor descriptor { get; } = new(
        new RuntimeSubsystemId("inno.runtime.input"),
        order: -1000);

    /// <summary>
    /// Creates an input feature over a newly allocated session backend.
    /// </summary>
    /// <param name="context">
    /// The isolated session context passed to the backend factory.
    /// </param>
    /// <returns>
    /// A new input feature that exclusively owns its backend.
    /// </returns>
    public IRuntimeSubsystem Create(RuntimeSubsystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        IInputBackend backend = m_backendFactory(context)
            ?? throw new InvalidOperationException("The input backend factory returned null.");
        return new InputRuntime(backend);
    }
}
