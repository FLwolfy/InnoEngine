namespace Inno.Engine.Scene;

/// <summary>
/// Base component for user code driven by scene lifecycle callbacks.
/// </summary>
public abstract class GameBehavior : GameComponent, ISceneLifecycleObject
{
    private bool m_enabled = true;

    /// <summary>
    /// Gets or sets whether this behavior participates in lifecycle updates.
    /// </summary>
    [Inno.Core.Serialization.SerializableProperty(Inno.Core.Serialization.PropertyVisibility.Hide)]
    public bool enabled
    {
        get => m_enabled;
        set => m_enabled = value;
    }

    /// <summary>
    /// Gets whether this behavior is enabled and active in its owning hierarchy.
    /// </summary>
    public bool isActiveAndEnabled
        => !isDestroyed && ownerOrNull is { activeInHierarchy: true } && m_enabled;

    /// <summary>
    /// Called once before this behavior first becomes active.
    /// </summary>
    protected virtual void Awake()
    {
    }

    /// <summary>
    /// Called once immediately before the first update of this behavior.
    /// </summary>
    protected virtual void Start()
    {
    }

    /// <summary>
    /// Called during the variable-rate update stage.
    /// </summary>
    /// <param name="deltaTime">Elapsed frame time in seconds.</param>
    protected virtual void Update(float deltaTime)
    {
    }

    /// <summary>
    /// Called during the fixed-rate update stage.
    /// </summary>
    /// <param name="fixedDeltaTime">Fixed simulation step in seconds.</param>
    protected virtual void FixedUpdate(float fixedDeltaTime)
    {
    }

    /// <summary>
    /// Called during the late update stage.
    /// </summary>
    /// <param name="deltaTime">Elapsed frame time in seconds.</param>
    protected virtual void LateUpdate(float deltaTime)
    {
    }

    /// <summary>
    /// Called when this behavior becomes active and enabled.
    /// </summary>
    protected virtual void OnEnable()
    {
    }

    /// <summary>
    /// Called when this behavior stops being active and enabled.
    /// </summary>
    protected virtual void OnDisable()
    {
    }

    /// <summary>
    /// Called immediately before this behavior is detached and destroyed.
    /// </summary>
    protected virtual void OnDestroy()
    {
    }

    internal bool lifecycleAwakeCalled { get; set; }
    internal bool lifecycleStartCalled { get; set; }
    internal bool lifecycleWasEnabled { get; set; }
    internal bool lifecycleDestroyCalled { get; set; }

    internal void DispatchAwake() => Awake();
    internal void DispatchStart() => Start();
    internal void DispatchUpdate(float deltaTime) => Update(deltaTime);
    internal void DispatchFixedUpdate(float fixedDeltaTime) => FixedUpdate(fixedDeltaTime);
    internal void DispatchLateUpdate(float deltaTime) => LateUpdate(deltaTime);
    internal void DispatchEnable() => OnEnable();
    internal void DispatchDisable() => OnDisable();
    internal void DispatchDestroy() => OnDestroy();

    bool ISceneLifecycleObject.lifecycleIsActive => isActiveAndEnabled;
    bool ISceneLifecycleObject.lifecycleIsDestroyed => isDestroyed;
    bool ISceneLifecycleObject.lifecycleAwakeCalled
    {
        get => lifecycleAwakeCalled;
        set => lifecycleAwakeCalled = value;
    }
    bool ISceneLifecycleObject.lifecycleStartCalled
    {
        get => lifecycleStartCalled;
        set => lifecycleStartCalled = value;
    }
    bool ISceneLifecycleObject.lifecycleWasEnabled
    {
        get => lifecycleWasEnabled;
        set => lifecycleWasEnabled = value;
    }
    bool ISceneLifecycleObject.lifecycleDestroyCalled
    {
        get => lifecycleDestroyCalled;
        set => lifecycleDestroyCalled = value;
    }
    void ISceneLifecycleObject.DispatchAwake() => Awake();
    void ISceneLifecycleObject.DispatchStart() => Start();
    void ISceneLifecycleObject.DispatchEnable() => OnEnable();
    void ISceneLifecycleObject.DispatchDisable() => OnDisable();
    void ISceneLifecycleObject.DispatchDestroy() => OnDestroy();
}
