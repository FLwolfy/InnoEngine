using Inno.Editor.Interactions;

namespace Inno.Editor.Scripting;

[EditorAction(ScriptingInteractionIds.C_RECOMPILE_SCRIPTING, ScriptingInteractionIds.C_MAIN_MENU_AREA)]
[EditorMenu(ScriptingInteractionIds.C_MAIN_MENU_AREA, "Scripting/Recompile Scripting", order: 100)]
internal sealed class RecompileScriptingAction(EditorScripting scripting) : EditorAction
{
    /// <summary>
    /// Evaluates whether the requested change can be applied to the current generation.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <returns>
    /// The validated editor action state that represents the completed operation.
    /// </returns>
    protected override EditorActionState Query(EditorActionContext context)
        => scripting.isAvailable ? EditorActionState.enabled : EditorActionState.disabled;

    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext context) => scripting.RecompileScripting();
}

[EditorAction(ScriptingInteractionIds.C_RELOAD_SCRIPTING, ScriptingInteractionIds.C_MAIN_MENU_AREA)]
[EditorMenu(ScriptingInteractionIds.C_MAIN_MENU_AREA, "Scripting/Reload Scripting", order: 110)]
internal sealed class ReloadScriptingAction(EditorScripting scripting) : EditorAction
{
    /// <summary>
    /// Evaluates whether the requested change can be applied to the current generation.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <returns>
    /// The validated editor action state that represents the completed operation.
    /// </returns>
    protected override EditorActionState Query(EditorActionContext context)
        => scripting.isAvailable ? EditorActionState.enabled : EditorActionState.disabled;

    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext context) => scripting.ReloadScripting();
}

[EditorAction(ScriptingInteractionIds.C_RELOAD_PLUGINS, ScriptingInteractionIds.C_MAIN_MENU_AREA)]
[EditorMenu(ScriptingInteractionIds.C_MAIN_MENU_AREA, "Scripting/Reload Plugins", order: 120)]
internal sealed class ReloadPluginsAction(EditorScripting scripting) : EditorAction
{
    /// <summary>
    /// Evaluates whether the requested change can be applied to the current generation.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <returns>
    /// The validated editor action state that represents the completed operation.
    /// </returns>
    protected override EditorActionState Query(EditorActionContext context)
        => scripting.isAvailable ? EditorActionState.enabled : EditorActionState.disabled;

    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext context) => scripting.ReloadPlugins();
}
