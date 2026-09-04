

using System.Collections.Generic;

using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Runtime;
using Inno.Scene;

namespace Inno.Editor.Panel.Inspector;

[EditorDrop(InspectorInteractionIds.C_ENGINE_OBJECT_REFERENCE_AREA, priority: 100)]
internal sealed class EngineObjectReferenceDropHandler(RuntimeSession runtimeSession)
    : EditorDrop<EngineObject, EngineObjectReferenceDropTarget>
{
    /// <summary>
    /// Evaluates whether the requested change can be applied to the current generation.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <returns>
    /// The validated editor drop status that represents the completed operation.
    /// </returns>
    protected override EditorDropStatus Query(
        EditorDropContext<EngineObject, EngineObjectReferenceDropTarget> context)
        => Resolve(context) is null
            ? EditorDropStatus.rejected
            : EditorDropStatus.Accept();

    /// <summary>
    /// Validates and applies the current editor drag-and-drop interaction atomically.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <returns>
    /// The validated editor drop result that represents the completed operation.
    /// </returns>
    protected override EditorDropResult Drop(
        EditorDropContext<EngineObject, EngineObjectReferenceDropTarget> context)
    {
        EngineObject? value = Resolve(context);
        if (value is null)
            return EditorDropResult.rejected;
        context.target.Assign(value);
        return EditorDropResult.Accepted();
    }

    private EngineObject? Resolve(
        EditorDropContext<EngineObject, EngineObjectReferenceDropTarget> context)
    {
        EngineObject source = context.source;
        if (source.isDestroyed)
        {
            return null;
        }
        if (source is GameObject sourceObject &&
            (!sourceObject.isRuntimeValid ||
             !ReferenceEquals(sourceObject.scene, runtimeSession.scenes.activeScene)))
        {
            return null;
        }
        if (context.target.expectedType.IsInstanceOfType(source))
            return source;
        if (source is not GameObject gameObject)
            return null;
        IReadOnlyList<GameComponent> components = gameObject.GetComponents();
        for (int i = 0; i < components.Count; i++)
        {
            if (context.target.expectedType.IsInstanceOfType(components[i]))
                return components[i];
        }
        return null;
    }
}
