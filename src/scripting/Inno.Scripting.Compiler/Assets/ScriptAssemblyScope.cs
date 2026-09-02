namespace Inno.Scripting.Compiler;

/// <summary>
/// Identifies whether a project script assembly can use editor-only APIs.
/// </summary>
public enum ScriptAssemblyScope
{
    /// <summary>
    /// The assembly can use runtime scripting APIs only.
    /// </summary>
    Runtime,

    /// <summary>
    /// The assembly can use runtime and editor scripting APIs.
    /// </summary>
    Editor
}
