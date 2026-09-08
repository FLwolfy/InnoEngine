using Inno.Runtime.Contracts;
using Inno.Scene;

namespace Inno.Runtime;

internal sealed class SceneRuntimeSubsystemFactory(SceneWorld scenes, RuntimeSessionKind kind) : IRuntimeSubsystemFactory
{
    internal static readonly RuntimeSubsystemId subsystemId = new("inno.runtime.scene");

    /// <summary>
    /// Gets the stable descriptor for the built-in scene simulation subsystem.
    /// </summary>
    public RuntimeSubsystemDescriptor descriptor { get; } = new(subsystemId, order: 0);

    /// <summary>
    /// Creates the scene subsystem for the isolated runtime session.
    /// </summary>
    /// <param name="context">
    /// The session context that supplies the scene world and session kind.
    /// </param>
    /// <returns>
    /// A new subsystem that advances the context's scene world.
    /// </returns>
    public IRuntimeSubsystem Create(RuntimeSubsystemContext context)
        => new SceneRuntimeSubsystem(scenes, kind);
}

internal sealed class SceneRuntimeSubsystem(SceneWorld scenes, RuntimeSessionKind sessionKind) : RuntimeSubsystem
{
    /// <summary>
    /// Advances non-editor scene systems by one deterministic interval.
    /// </summary>
    /// <param name="frame">
    /// The immutable fixed-step state.
    /// </param>
    protected override void OnFixedUpdate(RuntimeFixedFrame frame)
    {
        if (sessionKind != RuntimeSessionKind.Edit)
            scenes.FixedUpdate(frame.deltaTime);
    }

    /// <summary>
    /// Advances non-editor scene systems on the scaled variable clock.
    /// </summary>
    /// <param name="frame">
    /// The immutable variable-frame state.
    /// </param>
    protected override void OnUpdate(RuntimeFrame frame)
    {
        if (sessionKind != RuntimeSessionKind.Edit && !frame.isPaused)
            scenes.Update(frame.deltaTime);
    }

    /// <summary>
    /// Advances non-editor scene systems that depend on completed variable simulation.
    /// </summary>
    /// <param name="frame">
    /// The immutable variable-frame state.
    /// </param>
    protected override void OnLateUpdate(RuntimeFrame frame)
    {
        if (sessionKind != RuntimeSessionKind.Edit && !frame.isPaused)
            scenes.LateUpdate(frame.deltaTime);
    }
}
