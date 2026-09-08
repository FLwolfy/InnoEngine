using Inno.Animation.Runtime;
using Inno.Core.Diagnostics;
using Inno.Runtime.Contracts;

namespace Inno.Engine.Default;

internal static class AnimationSubsystem
{
    [RuntimeSubsystemRegistration("inno.runtime.animation")]
    internal static IRuntimeSubsystemFactory Create(EngineSessionComposition context)
        => new AnimationRuntimeFactory(owner =>
        {
            var reporter = owner.resources.Own(owner.diagnostics.CreateReporter(
                new DiagnosticSource($"inno.animation.bindings.{owner.identities.domainId}", "Animation")));
            var bindings = owner.resources.Own(new AnimationBindingRuntime(owner.types, reporter));
            return new AnimationRuntime(owner.events, bindings);
        });
}
