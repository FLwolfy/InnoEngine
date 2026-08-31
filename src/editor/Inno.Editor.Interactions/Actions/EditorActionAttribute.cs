using System;

namespace Inno.Editor.Interactions;

/// <summary>Registers an editor action for automatic discovery and dispatch.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EditorActionAttribute : Attribute
{
    /// <summary>Creates an action registration available from every area.</summary>
    /// <param name="action">The stable semantic action name.</param>
    /// <param name="priority">The tie-breaking priority used after target specificity.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="action"/> is empty.</exception>
    public EditorActionAttribute(string action, int priority = 0)
        : this(action, string.Empty, priority)
    {
    }

    /// <summary>Creates an action registration restricted to one exact area.</summary>
    /// <param name="action">The stable semantic action name.</param>
    /// <param name="area">The exact area, or an empty string for every area.</param>
    /// <param name="priority">The tie-breaking priority.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="action"/> is empty.</exception>
    public EditorActionAttribute(string action, string area, int priority = 0)
    {
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("An editor action name is required.", nameof(action));
        this.action = action;
        this.area = area ?? string.Empty;
        this.priority = priority;
    }

    /// <summary>Gets the stable action name.</summary>
    public string action { get; }

    /// <summary>Gets the optional exact interaction area.</summary>
    public string area { get; }

    /// <summary>Gets the tie-breaking priority.</summary>
    public int priority { get; }
}
