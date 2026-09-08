using System;

namespace Inno.Editor.Core;

/// <summary>
/// Registers an editor feature module with a stable identity for automatic discovery.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EditorModuleAttribute : Attribute
{
    /// <summary>
    /// Creates a module registration with stable identity and deterministic lifecycle order.
    /// </summary>
    /// <param name="id">
    /// The globally unique module identifier used for discovery diagnostics and optional persisted
    /// module state.
    /// </param>
    /// <param name="order">
    /// The ascending order used to start and update the module.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="id"/> is empty.
    /// </exception>
    public EditorModuleAttribute(string id, int order = 0)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("An editor module identifier is required.", nameof(id));
        this.id = id;
        this.order = order;
    }

    /// <summary>
    /// Gets the stable module identifier.
    /// </summary>
    public string id { get; }

    /// <summary>
    /// Gets the stable lifecycle order.
    /// </summary>
    public int order { get; }
}
