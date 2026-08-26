using System;
using System.Linq;
using System.Reflection;

using Inno.Core.Reflection;
using Inno.Editor.Interactions;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Inspector;

[EditorMenuSource(InspectorInteractionIds.C_SYSTEM_AREA)]
internal sealed class AddSystemMenuProvider : EditorMenuSource
{
    public override void Build(EditorMenuContext context, EditorMenuBuilder builder)
    {
        if (context.target is not GameScene scene)
            return;
        foreach (TypeRef typeRef in TypeCacheManager.GetSubTypesOf<GameSystem>())
        {
            Type type = typeRef.Resolve();
            if (!IsAddable(type, scene))
                continue;
            builder.Add(type.Name, InspectorInteractionIds.C_ADD_SYSTEM, argument: typeRef);
        }
    }

    private static bool IsAddable(Type type, GameScene scene)
    {
        if (type.IsAbstract || type.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null) is null)
            return false;
        return type.IsDefined(typeof(AllowMultipleSystemAttribute), inherit: false) ||
               !scene.GetSystems().Any(system => system.GetType() == type);
    }
}
