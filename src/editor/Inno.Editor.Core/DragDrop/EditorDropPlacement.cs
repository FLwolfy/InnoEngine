namespace Inno.Editor.Core.DragDrop;

/// <summary>Describes the requested placement relative to an editor drop target.</summary>
public enum EditorDropPlacement
{
    /// <summary>No positional placement is requested.</summary>
    None,

    /// <summary>Insert before the target.</summary>
    Before,

    /// <summary>Drop into the target.</summary>
    Into,

    /// <summary>Insert after the target.</summary>
    After
}
