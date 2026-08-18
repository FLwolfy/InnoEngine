using System;

namespace Inno.Core.Scripting;

/// <summary>
/// Maps a stable script-facing API namespace to one CLR implementation namespace.
/// </summary>
/// <remarks>
/// Multiple implementation namespaces can map to the same script API namespace. This keeps
/// script organization stable without introducing wrapper types with different runtime identities.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ScriptingApiNamespaceAttribute : Attribute
{
    /// <summary>
    /// Creates a script API namespace mapping.
    /// </summary>
    /// <param name="name">The stable script API namespace, such as <c>InnoEngine.Scene</c>.</param>
    /// <param name="implementationNamespace">The CLR namespace containing exported runtime types.</param>
    /// <param name="scope">The script profile that receives the mapping.</param>
    public ScriptingApiNamespaceAttribute(
        string name,
        string implementationNamespace,
        ScriptingApiScope scope)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A script API namespace is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(implementationNamespace))
            throw new ArgumentException("An implementation namespace is required.", nameof(implementationNamespace));
        this.name = name;
        this.implementationNamespace = implementationNamespace;
        this.scope = scope;
    }

    /// <summary>
    /// Gets the stable script API namespace.
    /// </summary>
    public string name { get; }

    /// <summary>
    /// Gets the CLR namespace that implements the script API namespace.
    /// </summary>
    public string implementationNamespace { get; }

    /// <summary>
    /// Gets the script profile that receives the mapping.
    /// </summary>
    public ScriptingApiScope scope { get; }
}
