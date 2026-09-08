using System;
using System.Linq;
using System.Reflection;

using Inno.Extensibility.Types;
using Inno.Editor.Interactions;
using Inno.Scene;

namespace Inno.Editor.Panel.Inspector;

[EditorMenuSource(InspectorInteractionIds.C_COMPONENT_AREA)]
internal sealed class AddComponentMenuProvider(TypeCatalog types) : EditorMenuSource
{
    /// <summary>
    /// Builds a validated result from the current immutable input snapshot.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="builder">
    /// The builder consumed by build; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void Build(EditorMenuContext context, EditorMenuBuilder builder)
    {
        if (context.target is not GameObject gameObject)
            return;
        TypeCacheSnapshot snapshot = types.current;
        foreach (TypeRef typeRef in snapshot.GetSubTypesOf<GameComponent>())
        {
            Type type = typeRef.Resolve(snapshot);
            if (!IsAddable(type, gameObject))
                continue;
            builder.Add(type.Name, InspectorInteractionIds.C_ADD_COMPONENT, argument: typeRef);
        }
    }

    private static bool IsAddable(Type type, GameObject gameObject)
    {
        if (type.IsAbstract || type.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null) is null)
            return false;
        return type.IsDefined(typeof(AllowMultipleComponentAttribute), inherit: false) ||
               !gameObject.GetComponents().Any(component => component.GetType() == type);
    }
}
