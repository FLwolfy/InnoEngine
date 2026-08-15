using Inno.Core.ECS;
using Inno.Core.Identity;
using Inno.Core.Serialization;

namespace Inno.Engine.Scene;

/// <summary>
/// Base class for user-facing scene behaviour components.
/// </summary>
public abstract class GameBehavior : Component, IIdentityObject, ISerializable
{
    private GameObject? m_gameObject;

    internal bool lifecycleAwakeCalled;
    internal bool lifecycleStartCalled;
    internal bool lifecycleWasEnabled;

    /// <summary>
    /// Gets the owning game object.
    /// </summary>
    public GameObject? gameObject => m_gameObject;

    /// <summary>
    /// Gets or sets whether this component receives lifecycle updates.
    /// </summary>
    [SerializableProperty(PropertyVisibility.Hide)]
    public bool enabled { get; set; } = true;

    /// <summary>
    /// Called once before the component's first active lifecycle update.
    /// </summary>
    public virtual void Awake()
    {
    }

    /// <summary>
    /// Called once before the component's first active update after Awake.
    /// </summary>
    public virtual void Start()
    {
    }

    /// <summary>
    /// Called during the variable timestep stage.
    /// </summary>
    /// <param name="deltaTime">Frame delta time in seconds.</param>
    public virtual void Update(float deltaTime)
    {
    }

    /// <summary>
    /// Called during the fixed timestep stage.
    /// </summary>
    /// <param name="fixedDeltaTime">Fixed timestep delta in seconds.</param>
    public virtual void FixedUpdate(float fixedDeltaTime)
    {
    }

    /// <summary>
    /// Called during the late variable timestep stage.
    /// </summary>
    /// <param name="deltaTime">Frame delta time in seconds.</param>
    public virtual void LateUpdate(float deltaTime)
    {
    }

    /// <summary>
    /// Called when the component is removed from active lifecycle tracking.
    /// </summary>
    public virtual void OnDestroy()
    {
    }

    /// <summary>
    /// Called when the component becomes enabled.
    /// </summary>
    public virtual void OnEnable()
    {
    }

    /// <summary>
    /// Called when the component becomes disabled.
    /// </summary>
    public virtual void OnDisable()
    {
    }

    /// <inheritdoc />
    public override void Reset()
    {
        lifecycleAwakeCalled = false;
        lifecycleStartCalled = false;
        lifecycleWasEnabled = false;
        enabled = true;

        m_gameObject = null;
    }

    internal virtual void BindGameObject(GameObject go)
    {
        m_gameObject = go;
    }
}
