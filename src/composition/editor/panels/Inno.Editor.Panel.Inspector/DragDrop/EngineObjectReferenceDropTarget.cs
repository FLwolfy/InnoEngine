
using System;

using Inno.Scene;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Provides an assignable engine-object-reference property drop target.
/// </summary>
public sealed class EngineObjectReferenceDropTarget
{
    private readonly Action<EngineObject> m_assign;
    private readonly Type m_expectedType;

    /// <summary>
    /// Creates a drop target that validates and assigns an engine object to a serialized property.
    /// </summary>
    /// <param name="expectedType">
    /// The engine object type accepted by the property.
    /// </param>
    /// <param name="assign">
    /// The callback that writes an accepted object to the property.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when either argument is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the type does not belong to the active type catalog.
    /// </exception>
    public EngineObjectReferenceDropTarget(Type expectedType, Action<EngineObject> assign)
    {
        ArgumentNullException.ThrowIfNull(expectedType);
        m_expectedType = expectedType;
        m_assign = assign ?? throw new ArgumentNullException(nameof(assign));
    }

    /// <summary>
    /// Gets the required engine object type.
    /// </summary>
    public Type expectedType => m_expectedType;

    /// <summary>
    /// Assigns an accepted engine object to the represented property.
    /// </summary>
    /// <param name="value">
    /// The concrete value read or transformed by this operation.
    /// </param>
    public void Assign(EngineObject value) => m_assign(value);
}
