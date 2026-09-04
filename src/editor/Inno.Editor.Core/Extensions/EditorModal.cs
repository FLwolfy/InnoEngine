using System.Numerics;

using Inno.Scripting.Api;

namespace Inno.Editor.Core;

/// <summary>
/// Defines non-dockable modal editor content.
/// </summary>
public abstract class EditorModal
{
    /// <summary>
    /// Gets whether the modal should currently be visible.
    /// </summary>
    public abstract bool isVisible { get; }

    /// <summary>
    /// Gets whether the modal prevents interaction with regular editor views.
    /// </summary>
    public virtual bool blocksInteraction => true;

    /// <summary>
    /// Gets whether the modal window can be moved inside the main viewport.
    /// </summary>
    public virtual bool canMove => false;

    /// <summary>
    /// Gets whether the modal window can be resized by the user.
    /// </summary>
    public virtual bool canResize => false;

    /// <summary>
    /// Gets the initial modal size in unscaled editor units, or <see cref="Vector2.Zero"/>
    /// when the runtime should size the modal from its content.
    /// </summary>
    public virtual Vector2 initialSize => Vector2.Zero;

    /// <summary>
    /// Gets the minimum modal size in unscaled editor units, or <see cref="Vector2.Zero"/>
    /// when no explicit minimum is required.
    /// </summary>
    public virtual Vector2 minimumSize => Vector2.Zero;

    /// <summary>
    /// Draws the modal body inside the runtime-managed centered window.
    /// </summary>
    /// <param name="context">
    /// The shared editor context containing current project and frame state.
    /// </param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    [ScriptingApiIgnore]
    public void Draw(EditorContext context)
    {
        System.ArgumentNullException.ThrowIfNull(context);
        OnDraw(context);
    }

    /// <summary>
    /// Draws the modal body inside the runtime-managed centered window.
    /// </summary>
    /// <param name="context">
    /// The shared editor context containing current project and frame state.
    /// </param>
    protected abstract void OnDraw(EditorContext context);
}
