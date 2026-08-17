using System;
using System.Collections.Generic;

namespace Inno.Engine.Scene;

/// <summary>
/// Base type for ordered scene-level systems that query the public component model.
/// </summary>
public abstract class GameSystem
{
    private GameScene? m_scene;

    /// <summary>
    /// Gets the ascending execution order used by the owning scene.
    /// </summary>
    public virtual int order => 0;

    /// <summary>
    /// Gets the owning scene after this system has been registered.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when this system is not registered.</exception>
    protected GameScene scene
        => m_scene ?? throw new InvalidOperationException($"System '{GetType().FullName}' is not registered with a scene.");

    /// <summary>
    /// Called during the fixed-rate scene stage.
    /// </summary>
    /// <param name="fixedDeltaTime">Fixed simulation step in seconds.</param>
    protected virtual void OnFixedUpdate(float fixedDeltaTime)
    {
    }

    /// <summary>
    /// Called during the variable-rate scene stage.
    /// </summary>
    /// <param name="deltaTime">Elapsed frame time in seconds.</param>
    protected virtual void OnUpdate(float deltaTime)
    {
    }

    /// <summary>
    /// Called during the late scene stage.
    /// </summary>
    /// <param name="deltaTime">Elapsed frame time in seconds.</param>
    protected virtual void OnLateUpdate(float deltaTime)
    {
    }

    /// <summary>
    /// Gets all scene components assignable to a requested type.
    /// </summary>
    /// <typeparam name="TComponent">Requested component type.</typeparam>
    /// <returns>A stable component snapshot.</returns>
    protected IReadOnlyList<TComponent> GetComponents<TComponent>() where TComponent : GameComponent
        => scene.GetComponents<TComponent>();

    /// <summary>
    /// Queries game objects containing one required component type.
    /// </summary>
    /// <typeparam name="T1">First required component type.</typeparam>
    /// <returns>A stable object snapshot.</returns>
    protected IReadOnlyList<GameObject> Query<T1>() where T1 : GameComponent
        => scene.Query(typeof(T1));

    /// <summary>
    /// Queries game objects containing two required component types.
    /// </summary>
    /// <typeparam name="T1">First required component type.</typeparam>
    /// <typeparam name="T2">Second required component type.</typeparam>
    /// <returns>A stable object snapshot.</returns>
    protected IReadOnlyList<GameObject> Query<T1, T2>()
        where T1 : GameComponent
        where T2 : GameComponent
        => scene.Query(typeof(T1), typeof(T2));

    /// <summary>
    /// Queries game objects containing three required component types.
    /// </summary>
    /// <typeparam name="T1">First required component type.</typeparam>
    /// <typeparam name="T2">Second required component type.</typeparam>
    /// <typeparam name="T3">Third required component type.</typeparam>
    /// <returns>A stable object snapshot.</returns>
    protected IReadOnlyList<GameObject> Query<T1, T2, T3>()
        where T1 : GameComponent
        where T2 : GameComponent
        where T3 : GameComponent
        => scene.Query(typeof(T1), typeof(T2), typeof(T3));

    internal void Attach(GameScene owner)
    {
        if (m_scene is not null)
            throw new InvalidOperationException($"System '{GetType().FullName}' is already registered.");
        m_scene = owner;
    }

    internal void Detach() => m_scene = null;
    internal void DispatchFixedUpdate(float fixedDeltaTime) => OnFixedUpdate(fixedDeltaTime);
    internal void DispatchUpdate(float deltaTime) => OnUpdate(deltaTime);
    internal void DispatchLateUpdate(float deltaTime) => OnLateUpdate(deltaTime);
}
