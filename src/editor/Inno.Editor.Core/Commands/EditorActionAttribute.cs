using System;

namespace Inno.Editor.Core.Commands;

/// <summary>Registers an editor action for automatic discovery and contextual dispatch.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EditorActionAttribute : Attribute
{
    /// <summary>
    /// Creates an action registration used by the editor extension catalog.
    /// </summary>
    /// <param name="id">The stable semantic identifier shared by all implementations of the action.</param>
    /// <param name="surface">The optional exact interaction surface handled by this implementation, or <see langword="null"/> for any surface.</param>
    /// <param name="priority">The tie-breaking priority used after surface and target specificity.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is empty.</exception>
    public EditorActionAttribute(string id, Type? surface = null, int priority = 0)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("An editor action identifier is required.", nameof(id));
        this.id = id;
        this.surface = surface;
        this.priority = priority;
    }

    /// <summary>Gets the stable action identifier.</summary>
    public string id { get; }

    /// <summary>Gets the optional exact interaction surface.</summary>
    public Type? surface { get; }

    /// <summary>Gets the tie-breaking priority.</summary>
    public int priority { get; }
}
