using System;

namespace Inno.Core.Scripting;

/// <summary>
/// Exports one public runtime type to a script compilation profile.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ScriptingApiExportAttribute : Attribute
{
    /// <summary>
    /// Creates an explicit script API export.
    /// </summary>
    /// <param name="type">The runtime type exposed to scripts.</param>
    /// <param name="scope">The script profile that receives the type.</param>
    public ScriptingApiExportAttribute(Type type, ScriptingApiScope scope)
    {
        this.type = type ?? throw new ArgumentNullException(nameof(type));
        this.scope = scope;
    }

    /// <summary>
    /// Gets the runtime type exposed to scripts.
    /// </summary>
    public Type type { get; }

    /// <summary>
    /// Gets the script profile that receives the type.
    /// </summary>
    public ScriptingApiScope scope { get; }
}
