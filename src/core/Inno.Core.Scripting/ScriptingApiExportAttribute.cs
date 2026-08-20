using System;

namespace Inno.Core.Scripting;

/// <summary>
/// Exports one public runtime type to a script compilation profile.
/// </summary>
/// <remarks>
/// The declaration assembly does not need to own the exported type. Feature projects may expose
/// selected types from lower-level dependencies while the generated runtime reference preserves
/// the exported type's original assembly identity.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ScriptingApiExportAttribute : Attribute
{
    /// <summary>
    /// Creates an explicit script API export.
    /// </summary>
    /// <param name="type">The runtime type exposed to scripts.</param>
    /// <param name="scope">The script profile that receives the type.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is <see langword="null"/>.</exception>
    public ScriptingApiExportAttribute(Type type, ScriptingApiScope scope)
        : this(type, GetDefaultName(type), scope)
    {
    }

    /// <summary>
    /// Creates an explicit script API export with a script-facing type name.
    /// </summary>
    /// <param name="type">The runtime type exposed to scripts.</param>
    /// <param name="name">The type name presented by the script facade.</param>
    /// <param name="scope">The script profile that receives the type.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is empty.</exception>
    public ScriptingApiExportAttribute(Type type, string name, ScriptingApiScope scope)
    {
        this.type = type ?? throw new ArgumentNullException(nameof(type));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A script-facing type name is required.", nameof(name));
        this.name = name;
        this.scope = scope;
    }

    /// <summary>
    /// Gets the runtime type exposed to scripts.
    /// </summary>
    public Type type { get; }

    /// <summary>
    /// Gets the type name presented by the script facade.
    /// </summary>
    public string name { get; }

    /// <summary>
    /// Gets the script profile that receives the type.
    /// </summary>
    public ScriptingApiScope scope { get; }

    private static string GetDefaultName(Type? type)
    {
        if (type is null)
            return string.Empty;
        int aritySeparator = type.Name.IndexOf('`');
        return aritySeparator < 0 ? type.Name : type.Name[..aritySeparator];
    }
}
