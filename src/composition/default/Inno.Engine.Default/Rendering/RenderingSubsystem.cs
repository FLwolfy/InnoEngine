using Inno.Rendering.Runtime;
using Inno.Runtime.Contracts;

namespace Inno.Engine.Default;

internal static class RenderingSubsystem
{
    [RuntimeSubsystemRegistration("inno.runtime.rendering")]
    internal static IRuntimeSubsystemFactory Create(RenderRuntime context)
        => new RenderRuntimeFactory(_ => context);
}
