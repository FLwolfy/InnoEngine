using Inno.Editor.Scene.DragDrop;

using Inno.Editor.Scene;

using System.Collections.Generic;

using Inno.Editor.Core;
using Inno.Editor.Core.DragDrop;
using Inno.Editor.Scene.Inspection;
using Inno.Editor.Scene.Workspace;
using Inno.Engine.Scene;

namespace Inno.Editor.Scene.Inspection.Interactions;

[EditorDrop(typeof(SceneSurface.EngineObjectReference), priority: 100)]
internal sealed class EngineObjectReferenceDropHandler
    : EditorDrop<EngineObject, EngineObjectReferenceDropTarget>
{
    private readonly EditorSceneWorkspace m_workspace;

    internal EngineObjectReferenceDropHandler(EditorSceneWorkspace workspace)
    {
        m_workspace = workspace;
    }

    protected override EditorDropStatus Query(
        EditorDropContext<EngineObject, EngineObjectReferenceDropTarget> context)
        => Resolve(context) is null
            ? EditorDropStatus.rejected
            : EditorDropStatus.Accept();

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
            (!sourceObject.isRuntimeValid || !ReferenceEquals(sourceObject.scene, m_workspace.activeScene)))
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
