namespace Inno.Engine.Scene;

internal interface ISceneLifecycleObject
{
    bool lifecycleIsActive { get; }
    bool lifecycleIsDestroyed { get; }
    bool lifecycleAwakeCalled { get; set; }
    bool lifecycleStartCalled { get; set; }
    bool lifecycleWasEnabled { get; set; }
    bool lifecycleDestroyCalled { get; set; }

    void DispatchAwake();
    void DispatchStart();
    void DispatchEnable();
    void DispatchDisable();
    void DispatchDestroy();
}
