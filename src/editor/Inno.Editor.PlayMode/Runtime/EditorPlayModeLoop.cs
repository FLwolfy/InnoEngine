using System;

namespace Inno.Editor.PlayMode;

/// <summary>
/// Bridges the host layer's fixed, variable, and late callbacks to the active Play Mode session.
/// </summary>
public sealed class EditorPlayModeLoop
{
    private EditorPlayModeModule? m_owner;

    /// <summary>Advances the active simulation by one fixed timestep when Play Mode is running.</summary>
    /// <param name="fixedDeltaTime">The fixed timestep in seconds.</param>
    public void FixedUpdate(float fixedDeltaTime) => m_owner?.FixedUpdate(fixedDeltaTime);

    /// <summary>Advances the active simulation by one variable timestep when Play Mode is running.</summary>
    /// <param name="deltaTime">The frame timestep in seconds.</param>
    public void Update(float deltaTime) => m_owner?.UpdateSimulation(deltaTime);

    /// <summary>Advances the active simulation's late phase when Play Mode is running.</summary>
    /// <param name="deltaTime">The frame timestep in seconds.</param>
    public void LateUpdate(float deltaTime) => m_owner?.LateUpdate(deltaTime);

    internal void Attach(EditorPlayModeModule owner)
    {
        if (m_owner is not null && !ReferenceEquals(m_owner, owner))
            throw new InvalidOperationException("The editor host already owns an active Play Mode module.");
        m_owner = owner;
    }

    internal void Detach(EditorPlayModeModule owner)
    {
        if (ReferenceEquals(m_owner, owner))
            m_owner = null;
    }
}
