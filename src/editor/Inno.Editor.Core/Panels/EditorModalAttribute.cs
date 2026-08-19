using System;

namespace Inno.Editor.Core.Panels;

/// <summary>Registers a centered editor modal for automatic discovery.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EditorModalAttribute : Attribute
{
    /// <summary>Creates a modal registration.</summary>
    public EditorModalAttribute(string id, string title, int order = 0)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("An editor modal identifier is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("An editor modal title is required.", nameof(title));
        this.id = id;
        this.title = title;
        this.order = order;
    }

    /// <summary>Gets the stable modal identifier.</summary>
    public string id { get; }

    /// <summary>Gets the visible modal title.</summary>
    public string title { get; }

    /// <summary>Gets the stable draw order.</summary>
    public int order { get; }
}
