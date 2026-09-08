using System;

namespace Inno.Runtime.Contracts;

/// <summary>
/// Declares a strongly typed composition method for build-time subsystem catalog generation.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RuntimeSubsystemRegistrationAttribute : Attribute
{
    /// <summary>
    /// Declares the stable ID returned by this method's subsystem factory.
    /// </summary>
    /// <param name="id">
    /// The exact non-empty subsystem descriptor ID.
    /// </param>
    public RuntimeSubsystemRegistrationAttribute(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id;
    }

    /// <summary>
    /// Gets the stable descriptor ID checked by the generated catalog.
    /// </summary>
    public string id { get; }
}
