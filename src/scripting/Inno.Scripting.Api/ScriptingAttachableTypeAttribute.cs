using System;

namespace Inno.Scripting.Api;

/// <summary>
/// Marks a script API base type whose concrete script-derived types require stable source-owned
/// identity metadata.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ScriptingAttachableTypeAttribute : Attribute
{
    /// <summary>
    /// Creates attachable script-type metadata for one extensible base class.
    /// </summary>
    /// <param name="kind">
    /// The non-empty domain-defined kind written to script type manifests and diagnostics.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="kind"/> is empty or contains only whitespace.
    /// </exception>
    public ScriptingAttachableTypeAttribute(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        this.kind = kind;
    }

    /// <summary>
    /// Gets the domain-defined attachable type kind recorded in script manifests.
    /// </summary>
    public string kind { get; }
}
