using Inno.Runtime.Contracts;
using Inno.UI.Runtime;

namespace Inno.Engine.Default;

internal static class UiSubsystem
{
    [RuntimeSubsystemRegistration("inno.runtime.ui")]
    internal static IRuntimeSubsystemFactory Create(EngineSessionComposition context)
        => new UiRuntimeFactory(_ => new UiRuntime(
            context.adapters.ui.CreateBackend(context.selection.ui),
            context.artifacts));
}
