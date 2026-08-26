using System;
using System.Linq;
using System.Reflection;

using Inno.Core.Reflection;
using Inno.Editor.Interactions;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Inspector;

[EditorMenuSource(InspectorInteractionIds.C_COMPONENT_AREA)]
internal sealed class AddComponentMenuProvider : EditorMenuSource
{
    public override void Build(EditorMenuContext context, EditorMenuBuilder builder)
    {
        if (context.target is not GameObject gameObject)
            return;
        foreach (TypeRef typeRef in TypeCacheManager.GetSubTypesOf<GameComponent>())
        {
            Type type = typeRef.Resolve();
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
