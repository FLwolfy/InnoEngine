namespace Inno.Scene;

internal static class SceneLifecycle
{
    internal static bool Prepare(ISceneLifecycleObject target, GameScene scene)
    {
        if (target.lifecycleIsDestroyed || target.lifecycleDestroyCalled)
            return false;

        bool active = target.lifecycleIsActive;
        if (active && !target.lifecycleAwakeCalled)
        {
            target.lifecycleAwakeCalled = true;
            target.DispatchAwake();
            if (!scene.canDispatch || target.lifecycleIsDestroyed)
                return false;
        }

        return PrepareActivation(target, scene);
    }

    private static bool PrepareActivation(ISceneLifecycleObject target, GameScene scene)
    {
        bool active = target.lifecycleIsActive;
        if (active && !target.lifecycleWasEnabled)
        {
            target.lifecycleWasEnabled = true;
            target.DispatchEnable();
            if (!scene.canDispatch || target.lifecycleIsDestroyed)
                return false;
        }
        else if (!active && target.lifecycleWasEnabled)
        {
            target.lifecycleWasEnabled = false;
            target.DispatchDisable();
            if (!scene.canDispatch || target.lifecycleIsDestroyed)
                return false;
        }

        return true;
    }

    internal static void DisableForReload(ISceneLifecycleObject target)
        => Disable(target);

    private static void Disable(ISceneLifecycleObject target)
    {
        if (!target.lifecycleWasEnabled)
            return;
        target.lifecycleWasEnabled = false;
        target.DispatchDisable();
    }

    internal static void Destroy(ISceneLifecycleObject target)
    {
        if (target.lifecycleDestroyCalled)
            return;
        target.lifecycleDestroyCalled = true;
        if (target.lifecycleWasEnabled)
        {
            target.lifecycleWasEnabled = false;
            target.DispatchDisable();
        }
        if (target.lifecycleAwakeCalled)
            target.DispatchDestroy();
    }

}
