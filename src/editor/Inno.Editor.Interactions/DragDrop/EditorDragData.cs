using System;

namespace Inno.Editor.Interactions.DragDrop;

/// <summary>Contains the managed source object and label for one editor drag operation.</summary>
public sealed class EditorDragData
{
    private readonly Func<bool>? m_isValid;

    /// <summary>
    /// Creates managed drag data whose native payload is represented by a runtime-owned token.
    /// </summary>
    /// <param name="source">The managed object exposed to typed drop handlers.</param>
    /// <param name="label">The human-readable label used by drag previews.</param>
    /// <param name="isValid">An optional predicate evaluated while dragging to reject stale source objects.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is <see langword="null"/>.</exception>
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
