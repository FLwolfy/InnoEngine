using Inno.Editor.Scene.DragDrop;

using System;

using Inno.Engine.Scene;

namespace Inno.Editor.Scene.DragDrop;

/// <summary>Provides an assignable engine-object-reference property drop target.</summary>
public sealed class EngineObjectReferenceDropTarget
{
    private readonly Action<EngineObject> m_assign;

    /// <summary>
    /// Creates a drop target that validates and assigns an engine object to a serialized property.
    /// </summary>
    /// <param name="expectedType">The engine object type accepted by the property.</param>
    /// <param name="assign">The callback that writes an accepted object to the property.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null"/>.</exception>
    public EngineObjectReferenceDropTarget(Type expectedType, Action<EngineObject> assign)
    {
        this.expectedType = expectedType ?? throw new ArgumentNullException(nameof(expectedType));
        m_assign = assign ?? throw new ArgumentNullException(nameof(assign));
    }

    /// <summary>Gets the required engine object type.</summary>
    public Type expectedType { get; }

    /// <summary>
    /// Assigns an accepted engine object to the represented property.
    /// </summary>
    /// <param name="value">The compatible live engine object.</param>
    public void Assign(EngineObject value) => m_assign(value);
}
