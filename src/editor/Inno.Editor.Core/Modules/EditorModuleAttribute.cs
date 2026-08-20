using System;

namespace Inno.Editor.Core;

/// <summary>Registers an editor feature module for automatic discovery.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EditorModuleAttribute : Attribute
{
    /// <summary>
    /// Creates a module registration with a deterministic lifecycle order.
    /// </summary>
    /// <param name="order">The ascending order used to start and update the module.</param>
    public EditorModuleAttribute(int order = 0)
    {
        this.order = order;
    }

    /// <summary>Gets the stable lifecycle order.</summary>
    public int order { get; }
}
