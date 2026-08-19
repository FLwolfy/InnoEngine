using System;
using System.Linq;
using System.Reflection;

using Inno.Core.Reflection;
using Inno.Editor.Core.Menus;
using Inno.Engine.Scene;

namespace Inno.Editor.Scene.Menus;

[EditorMenuSource(typeof(SceneSurface.AddSystem))]
internal sealed class AddSystemMenuProvider : EditorMenuSource
{
    public override void Build(EditorMenuContext context, EditorMenuBuilder builder)
    {
        if (context.target is not GameScene scene)
            return;
        foreach (Type type in TypeCacheManager.GetSubTypesOf<GameSystem>())
        {
            if (!IsAddable(type, scene))
                continue;
            builder.Add(type.Name, SceneActionIds.AddSystem, argument: type);
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
