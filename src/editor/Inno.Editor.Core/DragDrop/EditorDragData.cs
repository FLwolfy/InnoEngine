using System;

namespace Inno.Editor.Core.DragDrop;

/// <summary>Contains the managed source object and label for one editor drag operation.</summary>
public sealed class EditorDragData
{
    private readonly Func<bool>? m_isValid;

    /// <summary>Creates managed editor drag data.</summary>
    public EditorDragData(object source, string label, Func<bool>? isValid = null)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.label = label ?? string.Empty;
        m_isValid = isValid;
    }

    /// <summary>Gets the managed drag source.</summary>
    public object source { get; }

    /// <summary>Gets the drag preview label.</summary>
    public string label { get; }

    /// <summary>Gets whether the source remains valid for the current drag session.</summary>
    public bool isValid => m_isValid?.Invoke() ?? true;
}
