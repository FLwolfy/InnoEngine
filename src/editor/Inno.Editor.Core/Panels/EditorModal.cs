namespace Inno.Editor.Core.Panels;

/// <summary>Defines non-dockable modal editor content.</summary>
public abstract class EditorModal
{
    /// <summary>Gets whether the modal should currently be visible.</summary>
    public abstract bool isVisible { get; }

    /// <summary>Gets whether the modal prevents interaction with regular editor views.</summary>
    public virtual bool blocksInteraction => true;

    /// <summary>
    /// Draws the modal body inside the runtime-managed centered window.
    /// </summary>
    /// <param name="context">The shared editor context containing current project and frame state.</param>
    public abstract void Draw(EditorContext context);
}
