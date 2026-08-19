using System;

namespace Inno.Editor.Core.Commands;

/// <summary>Registers an editor action for automatic discovery and contextual dispatch.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EditorActionAttribute : Attribute
{
    /// <summary>Creates an action registration.</summary>
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
