namespace Inno.Scene;

internal interface ISceneLifecycleObject
{
    /// <summary>
    /// Gets whether the object currently satisfies its scene activation conditions.
    /// </summary>
    bool lifecycleIsActive { get; }

    /// <summary>
    /// Gets whether the object has been permanently detached from its scene owner.
    /// </summary>
    bool lifecycleIsDestroyed { get; }

    /// <summary>
    /// Gets or sets whether the enable callback has been dispatched for the current activation.
    /// </summary>
    bool lifecycleWasEnabled { get; set; }

    /// <summary>
    /// Gets or sets whether the one-time Awake callback has already been dispatched.
    /// </summary>
    bool lifecycleAwakeCalled { get; set; }
    /// <summary>
    /// Gets or sets whether the one-time Start callback has already been dispatched.
    /// </summary>
    bool lifecycleStartCalled { get; set; }
    /// <summary>
    /// Gets or sets whether the terminal destruction callback has already been dispatched.
    /// </summary>
    bool lifecycleDestroyCalled { get; set; }

    /// <summary>
    /// Dispatches the one-time Awake lifecycle callback to the scene object.
    /// </summary>
    void DispatchAwake();

    /// <summary>
    /// Dispatches the one-time Start lifecycle callback before regular updates begin.
    /// </summary>
    void DispatchStart();

    /// <summary>
    /// Dispatches the enable callback immediately after the object becomes active.
    /// </summary>
    void DispatchEnable();

    /// <summary>
    /// Dispatches the disable callback immediately before the object becomes inactive.
    /// </summary>
    void DispatchDisable();

    /// <summary>
    /// Dispatches the terminal destruction callback before scene storage releases the object.
    /// </summary>
    void DispatchDestroy();
}
