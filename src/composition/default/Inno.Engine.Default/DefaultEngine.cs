using System.Collections.Generic;
using Inno.Rendering.Runtime;
using Inno.Runtime.Contracts;

namespace Inno.Engine.Default;

/// <summary>
/// Provides build-generated default engine assembly, independent of Editor and Player loop implementations.
/// </summary>
public static partial class DefaultEngine
{
    /// <summary>
    /// Creates the complete default session factory set from local subsystem declarations.
    /// </summary>
    /// <param name="context">
    /// The strongly typed composition inputs borrowed during factory creation.
    /// </param>
    /// <returns>
    /// A frozen deterministic factory list; the receiving RuntimeSession owns the created subsystems.
    /// </returns>
    [RuntimeSubsystemCatalog]
    public static partial IReadOnlyList<IRuntimeSubsystemFactory> CreateSessionSubsystems(EngineSessionComposition context);

    /// <summary>
    /// Creates the default host factory set over the product-configured rendering runtime.
    /// </summary>
    /// <param name="context">
    /// The rendering runtime whose domain resources are owned by this host.
    /// </param>
    /// <returns>
    /// A frozen host-lifetime factory list that is never inserted into a session pipeline.
    /// </returns>
    [RuntimeSubsystemCatalog]
    public static partial IReadOnlyList<IRuntimeSubsystemFactory> CreateHostSubsystems(RenderRuntime context);
}
