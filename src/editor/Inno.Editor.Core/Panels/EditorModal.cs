namespace Inno.Editor.Core.Panels;

/// <summary>Defines non-dockable modal editor content.</summary>
public abstract class EditorModal
{
    /// <summary>Gets whether the modal should currently be visible.</summary>
    public abstract bool isVisible { get; }

    /// <summary>Gets whether the modal prevents interaction with regular editor views.</summary>
    public virtual bool blocksInteraction => true;

    /// <summary>Draws modal content.</summary>
    public abstract void Draw(EditorContext context);
}
