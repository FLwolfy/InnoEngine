namespace Inno.Editor.Core.DragDrop;

/// <summary>Defines the standard visual used for a compatible drop target.</summary>
public enum EditorDropVisual
{
    /// <summary>No visual is drawn.</summary>
    None,

    /// <summary>Draw a content highlight.</summary>
    Highlight,

    /// <summary>Draw an insertion line before the target.</summary>
    InsertBefore,

    /// <summary>Draw an insertion line after the target.</summary>
    InsertAfter,

    /// <summary>Draw a disabled target visual.</summary>
    Disabled
}
