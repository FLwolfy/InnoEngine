using System;
using System.Linq;
using System.Reflection;

using Inno.Core.Reflection;
using Inno.Editor.Interactions;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Inspector;

[EditorMenuSource("panel/scene.inspector/component")]
internal sealed class AddComponentMenuProvider : EditorMenuSource
{
    public override void Build(EditorMenuContext context, EditorMenuBuilder builder)
    {
        if (context.target is not GameObject gameObject)
            return;
        foreach (Type type in TypeCacheManager.GetSubTypesOf<GameComponent>())
        {
            if (!IsAddable(type, gameObject))
                continue;
            builder.Add(type.Name, "inspector/add-component", argument: type);
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
