
using System;
using System.Numerics;

using Inno.Core.Execution;
using Inno.Scripting.Api;

namespace Inno.Editor.Core;

/// <summary>
/// Base class for editor panel implementations.
/// </summary>
public abstract class EditorPanel
{
    private bool m_attached;
    private bool m_ready;

    /// <summary>
    /// Gets whether the presentation backend should inset this panel body by its standard
    /// window padding.
    /// </summary>
    public virtual bool useWindowPadding => true;

    /// <summary>
    /// Gets whether the presentation backend may create a scroll range for this panel window.
    /// </summary>
    /// <remarks>
    /// Full-canvas panels should return <see langword="false"/> and manage navigation inside their
    /// own content instead of allowing the host window to scroll.
    /// </remarks>
    public virtual bool allowScrolling => true;

    /// <summary>Gets the initial floating size in UI coordinates; zero leaves native auto-sizing in control. Saved layouts take precedence.</summary>
    public virtual Vector2 initialSize => Vector2.Zero;

    /// <summary>
    /// Gets or sets whether panel is visible.
    /// </summary>
    public bool isOpen { get; set; } = true;

    /// <summary>
    /// Attaches the panel after its extension generation becomes active.
    /// </summary>
    /// <param name="context">
    /// The shared editor context for the active runtime.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the panel is already attached.
    /// </exception>
    [ScriptingApiIgnore]
    public void Attach(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (m_attached)
            throw new InvalidOperationException($"Editor panel '{GetType().FullName}' is already attached.");
        m_attached = true;
        OnAttach(context);
        m_ready = true;
    }

    /// <summary>
    /// Detaches the panel before its extension generation is released.
    /// </summary>
    /// <remarks>
    /// Also compensates partial attachment. Completed detachment is idempotent; a pending panel cannot draw.
    /// </remarks>
    /// <param name="context">
    /// The shared editor context for the runtime being detached.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="RetirementPendingException">
    /// Panel work is still active. The owner must retain the panel and its dependencies and retry detachment.
    /// </exception>
    [ScriptingApiIgnore]
    public void Detach(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!m_attached)
            return;
        m_ready = false;
        try { OnDetach(context); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch
        {
            m_attached = false;
            throw;
        }
        m_attached = false;
    }

    /// <summary>
    /// Draws the complete dockable contents of the panel for the current frame.
    /// </summary>
    /// <param name="context">
    /// The shared editor context containing current selection, focus, and frame state.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the panel is not attached.
    /// </exception>
    [ScriptingApiIgnore]
    public void Draw(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!m_ready)
            throw new InvalidOperationException($"Editor panel '{GetType().FullName}' is not attached.");
        OnDraw(context);
    }

    /// <summary>
    /// Draws the complete dockable contents of this panel for the current frame.
    /// </summary>
    /// <param name="context">
    /// The shared editor context containing current selection, focus, and frame state.
    /// </param>
    protected abstract void OnDraw(EditorContext context);

    /// <summary>
    /// Runs after the panel is attached to an active extension generation.
    /// </summary>
    /// <param name="context">
    /// The shared editor context for the active runtime.
    /// </param>
    protected virtual void OnAttach(EditorContext context)
    {
    }

    /// <summary>
    /// Runs before the panel is detached from its active extension generation.
    /// </summary>
    /// <remarks>
    /// This includes failed attachment compensation. Retain unfinished resources when throwing
    /// RetirementPendingException and avoid repeating completed cleanup steps on the next attempt.
    /// </remarks>
    /// <param name="context">
    /// The shared editor context for the runtime being detached.
    /// </param>
    protected virtual void OnDetach(EditorContext context)
    {
    }

    /// <summary>
    /// Captures readable project state owned by this panel.
    /// </summary>
    /// <remarks>
    /// Overriding this method opts the panel into project-state IO. Panels that keep the base
    /// implementation are never registered with the persistence coordinator and therefore perform
    /// no state reads or writes. Panel visibility is persisted separately for every registered panel.
    /// </remarks>
    /// <param name="state">
    /// The writable parameter that receives the complete readable state for this panel.
    /// </param>
    protected virtual void Capture(EditorState state)
    {
    }

    /// <summary>
    /// Restores readable project state owned by this panel.
    /// </summary>
    /// <remarks>
    /// This method is called only when <see cref="Capture"/> is overridden. It runs once after the
    /// panel is attached and before the panel is allowed to capture replacement state.
    /// </remarks>
    /// <param name="state">
    /// The read-only state parameter for this panel. Missing or incompatible values return the
    /// fallback supplied to <see cref="EditorState.Get{T}(string, T)"/>.
    /// </param>
    protected virtual void Restore(EditorState state)
    {
    }
}
