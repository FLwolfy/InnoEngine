using System;
using System.Collections.Generic;

using Inno.Core.Serialization;

namespace Inno.Engine.Scene;

/// <summary>
/// Base type for serializable, ordered scene-level behaviors.
/// </summary>
public abstract class GameSystem : EngineObject, ISerializable, ISceneLifecycleObject
{
    private GameScene? m_scene;
    private bool m_enabled = true;

    /// <summary>
    /// Gets or sets whether this system participates in scene lifecycle updates.
    /// </summary>
    [SerializableProperty(PropertyVisibility.Hide)]
    public bool enabled
    {
        get => m_enabled;
        set => m_enabled = value && this is not MissingGameSystem;
    }

    /// <summary>
    /// Gets whether this system is registered, enabled, and dispatchable.
    /// </summary>
    public bool isActiveAndEnabled
        => !isDestroyed && m_scene is { canDispatch: true } && m_enabled;

    /// <summary>
    /// Gets the ascending execution order used by the owning scene.
    /// </summary>
    public virtual int order => 0;

    /// <summary>
    /// Gets the owning scene after this system has been registered.
    /// </summary>
    protected GameScene scene
        => m_scene ?? throw new InvalidOperationException(
            $"System '{GetType().FullName}' is not registered with a scene.");

    /// <summary>Restores this system to defaults when added or explicitly reset.</summary>
    protected virtual void Reset()
    {
    }

    /// <summary>Called once before the system first becomes active.</summary>
    protected virtual void Awake()
    {
    }

    /// <summary>Called once immediately before the first update.</summary>
    protected virtual void Start()
    {
    }

    /// <summary>Called when the system becomes active and enabled.</summary>
    protected virtual void OnEnable()
    {
    }

    /// <summary>Called when the system stops being active and enabled.</summary>
    protected virtual void OnDisable()
    {
    }

    /// <summary>Called before a system that entered runtime lifecycle is destroyed.</summary>
    protected virtual void OnDestroy()
    {
    }

    /// <summary>Called during the fixed-rate scene stage. Use <c>Time.fixedDeltaTime</c> for step timing.</summary>
    protected virtual void OnFixedUpdate()
    {
    }

    /// <summary>Called during the variable-rate scene stage. Use <c>Time.deltaTime</c> for frame timing.</summary>
    protected virtual void OnUpdate()
    {
    }

    /// <summary>Called during the late scene stage. Use <c>Time.deltaTime</c> for frame timing.</summary>
    protected virtual void OnLateUpdate()
    {
    }

    /// <summary>Gets all scene components assignable to a requested type.</summary>
    protected IReadOnlyList<TComponent> GetComponents<TComponent>() where TComponent : GameComponent
        => scene.GetComponents<TComponent>();

    /// <summary>Queries game objects containing one required component type.</summary>
    protected IReadOnlyList<GameObject> Query<T1>() where T1 : GameComponent
        => scene.Query<T1>();

    /// <summary>Queries game objects containing two required component types.</summary>
    protected IReadOnlyList<GameObject> Query<T1, T2>()
        where T1 : GameComponent
        where T2 : GameComponent
        => scene.Query<T1, T2>();

    /// <summary>Queries game objects containing three required component types.</summary>
    protected IReadOnlyList<GameObject> Query<T1, T2, T3>()
        where T1 : GameComponent
        where T2 : GameComponent
        where T3 : GameComponent
        => scene.Query<T1, T2, T3>();

    internal bool lifecycleAwakeCalled { get; set; }
    internal bool lifecycleStartCalled { get; set; }
    internal bool lifecycleWasEnabled { get; set; }
    internal bool lifecycleDestroyCalled { get; set; }
    internal GameScene? ownerScene => m_scene;

    internal void Attach(GameScene owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (m_scene is not null)
            throw new InvalidOperationException($"System '{GetType().FullName}' is already registered.");
        m_scene = owner;
    }

    internal void Detach()
    {
        m_scene = null;
        MarkDestroyed();
    }

    internal void DispatchReset() => Reset();
    internal void DispatchFixedUpdate() => OnFixedUpdate();
    internal void DispatchUpdate() => OnUpdate();
    internal void DispatchLateUpdate() => OnLateUpdate();

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
