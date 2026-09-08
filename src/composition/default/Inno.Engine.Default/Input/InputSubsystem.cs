using Inno.Input.Runtime;
using Inno.Runtime.Contracts;

namespace Inno.Engine.Default;

internal static class InputSubsystem
{
    [RuntimeSubsystemRegistration("inno.runtime.input")]
    internal static IRuntimeSubsystemFactory Create(EngineSessionComposition context)
        => new InputRuntimeFactory(_ => context.inputSource.CreateBackend());
}
