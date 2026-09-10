namespace Inno.Scripting.Api;

/// <summary>
/// Identifies the script compilation profile that receives an exported API.
/// </summary>
public enum ScriptingApiScope
{
    /// <summary>
    /// Exposes the API to both game and editor scripts.
    /// </summary>
    Runtime,

    /// <summary>
    /// Exposes the API only to editor scripts.
    /// </summary>
    Editor,

    /// <summary>
    /// Exposes compile-time annotations to game and editor scripts. Player compilation erases
    /// their applications and derived annotation declarations before binding to target assemblies.
    /// </summary>
    Authoring
}
