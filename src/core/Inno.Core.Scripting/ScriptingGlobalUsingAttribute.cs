using System;

namespace Inno.Core.Scripting;

/// <summary>
/// Imports one declared script API namespace into generated script compilations.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ScriptingGlobalUsingAttribute : Attribute
{
    /// <summary>
    /// Creates a generated script global using declaration.
    /// </summary>
    /// <param name="namespaceName">A script API namespace declared by <see cref="ScriptingApiNamespaceAttribute"/>.</param>
    /// <param name="scope">The script profile that receives the global using.</param>
    public ScriptingGlobalUsingAttribute(string namespaceName, ScriptingApiScope scope)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
            throw new ArgumentException("A script API namespace is required.", nameof(namespaceName));
        this.namespaceName = namespaceName;
        this.scope = scope;
    }

    /// <summary>
    /// Gets the script API namespace to import.
    /// </summary>
    public string namespaceName { get; }

    /// <summary>
    /// Gets the script profile that receives the global using.
    /// </summary>
    public ScriptingApiScope scope { get; }
}
