using System;

namespace Inno.Editor.PlayMode;

/// <summary>
/// Bridges the editor host frame clock to the active Play Mode session.
/// </summary>
public sealed class EditorPlayModeLoop
{
    private static readonly IDisposable S_EMPTY_SCOPE = new EmptyScope();

    private EditorPlayModeController? m_owner;

    /// <summary>
    /// Advances the active simulation by one complete frame when Play Mode is running.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed frame time in seconds.
    /// </param>
    public void Tick(float deltaTime) => m_owner?.Tick(deltaTime);

    /// <summary>
    /// Binds editor presentation and interaction work to the isolated Play session while simulation is
    /// active, or returns an inert scope while the editor is not presenting runtime copies.
    /// </summary>
    /// <returns>
    /// A scope that must be disposed after the current editor presentation phase completes.
    /// </returns>
    public IDisposable EnterPresentationScope()
        => m_owner?.EnterPresentationScope() ?? S_EMPTY_SCOPE;

    internal void Attach(EditorPlayModeController owner)
    {
        if (m_owner is not null && !ReferenceEquals(m_owner, owner))
            throw new InvalidOperationException("The editor host already owns an active Play Mode module.");
        m_owner = owner;
    }

    internal void Detach(EditorPlayModeController owner)
    {
        if (ReferenceEquals(m_owner, owner))
            m_owner = null;
    }

    private sealed class EmptyScope : IDisposable
    {
        /// <summary>
        /// Completes the inert presentation scope.
        /// </summary>
        public void Dispose()
        {
        }
    }
}
