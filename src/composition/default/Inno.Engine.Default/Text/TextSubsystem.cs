using Inno.Runtime.Contracts;
using Inno.Text.Runtime;

namespace Inno.Engine.Default;

internal static class TextSubsystem
{
    [RuntimeSubsystemRegistration("inno.runtime.text")]
    internal static IRuntimeSubsystemFactory Create(EngineSessionComposition context)
        => new TextRuntimeFactory(_ => new TextRuntime(
            context.adapters.text.CreateBackend(context.selection.text),
            context.artifacts));
}
