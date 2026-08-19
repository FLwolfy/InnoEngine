using Inno.Editor.Scene.DragDrop;

using System;

using Inno.Engine.Scene;

namespace Inno.Editor.Scene.DragDrop;

/// <summary>Provides an assignable engine-object-reference property drop target.</summary>
public sealed class EngineObjectReferenceDropTarget
{
    private readonly Action<EngineObject> m_assign;

    /// <summary>Creates an engine-object-reference drop target.</summary>
    public EngineObjectReferenceDropTarget(Type expectedType, Action<EngineObject> assign)
    {
        this.expectedType = expectedType ?? throw new ArgumentNullException(nameof(expectedType));
        m_assign = assign ?? throw new ArgumentNullException(nameof(assign));
    }

    /// <summary>Gets the required engine object type.</summary>
    public Type expectedType { get; }

    /// <summary>Assigns an engine object to the property.</summary>
    public void Assign(EngineObject value) => m_assign(value);
}
