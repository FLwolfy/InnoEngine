using System;
using System.Collections.Generic;
using Inno.Core.Identity;
using Inno.Core.Mathematics;
using Inno.References;

namespace Inno.Editor.Rendering;

/// <summary>
/// Provides scene content and selection for transient Editor gizmos.
/// </summary>
public sealed class EditorGizmoContext
{
    /// <summary>
    /// Creates a frame-scoped gizmo request.
    /// </summary>
    /// <param name="content">
    /// The scenes presented by the Scene viewport.
    /// </param>
    /// <param name="selected">
    /// The selected live object, when available.
    /// </param>
    /// <param name="pixelWidth">
    /// The Scene viewport width.
    /// </param>
    /// <param name="pixelHeight">
    /// The Scene viewport height.
    /// </param>
    public EditorGizmoContext(ContentReadScope content, RuntimeIdentity? selected,
        int pixelWidth, int pixelHeight)
    {
        this.content = content ?? throw new ArgumentNullException(nameof(content));
        this.selected = selected;
        this.pixelWidth = pixelWidth;
        this.pixelHeight = pixelHeight;
    }

    /// <summary>
    /// Gets the scene roots visible in this Editor viewport.
    /// </summary>
    public ContentReadScope content { get; }
    /// <summary>
    /// Gets the transient selected identity.
    /// </summary>
    public RuntimeIdentity? selected { get; }
    /// <summary>
    /// Gets the viewport width in physical pixels.
    /// </summary>
    public int pixelWidth { get; }
    /// <summary>
    /// Gets the viewport height in physical pixels.
    /// </summary>
    public int pixelHeight { get; }
}

/// <summary>
/// Receives transient world-space gizmo geometry from independent Editor extensions.
/// </summary>
public interface IEditorGizmoSink
{
    /// <summary>
    /// Adds a visible, selectable icon at an object's world position.
    /// </summary>
    /// <param name="owner">
    /// The scene object selected when the icon is clicked.
    /// </param>
    /// <param name="position">
    /// The icon position in world space.
    /// </param>
    /// <param name="glyph">
    /// A short symbol displayed inside the icon.
    /// </param>
    /// <param name="color">
    /// The icon tint.
    /// </param>
    void Icon(Identity owner, Vector3 position, string glyph, Color color);

    /// <summary>
    /// Adds a non-interactive world-space line, normally for selected bounds.
    /// </summary>
    /// <param name="start">
    /// The first world-space endpoint.
    /// </param>
    /// <param name="end">
    /// The second world-space endpoint.
    /// </param>
    /// <param name="color">
    /// The line tint.
    /// </param>
    void Line(Vector3 start, Vector3 end, Color color);
}

/// <summary>
/// Contributes Editor-only icons and bounds without modifying a rendering model.
/// </summary>
public abstract class EditorGizmoProvider
{
    /// <summary>
    /// Submits transient icons and lines for the presented scenes.
    /// </summary>
    /// <param name="context">
    /// The current Scene viewport request.
    /// </param>
    /// <param name="sink">
    /// The collector that owns submitted primitives for this frame.
    /// </param>
    public abstract void Collect(EditorGizmoContext context, IEditorGizmoSink sink);
}

/// <summary>
/// Marks one reloadable Editor gizmo provider.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EditorGizmoProviderExtensionAttribute : Attribute
{
    /// <summary>
    /// Creates a provider declaration with a stable identifier.
    /// </summary>
    /// <param name="id">
    /// The globally unique provider identifier.
    /// </param>
    public EditorGizmoProviderExtensionAttribute(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id;
    }

    /// <summary>
    /// Gets the stable provider identifier.
    /// </summary>
    public string id { get; }
}

/// <summary>
/// One selectable Editor icon.
/// </summary>
/// <param name="owner">
/// The object that owns the resulting lifetime and state.
/// </param>
/// <param name="position">
/// The vector3 value used to initialize this instance.
/// </param>
/// <param name="glyph">
/// The string value used to initialize this instance.
/// </param>
/// <param name="color">
/// The color value used to initialize this instance.
/// </param>
public readonly record struct EditorGizmoIcon(Identity owner, Vector3 position, string glyph, Color color);

/// <summary>
/// One non-interactive Editor line.
/// </summary>
/// <param name="start">
/// The vector3 value used to initialize this instance.
/// </param>
/// <param name="end">
/// The vector3 value used to initialize this instance.
/// </param>
/// <param name="color">
/// The color value used to initialize this instance.
/// </param>
public readonly record struct EditorGizmoLine(Vector3 start, Vector3 end, Color color);

/// <summary>
/// Holds the transient gizmo primitives for one Scene viewport frame.
/// </summary>
public sealed class EditorGizmoFrame : IEditorGizmoSink
{
    private readonly List<EditorGizmoIcon> m_icons = [];
    private readonly List<EditorGizmoLine> m_lines = [];

    /// <summary>
    /// Gets the ordered selectable icons.
    /// </summary>
    public IReadOnlyList<EditorGizmoIcon> icons => m_icons;
    /// <summary>
    /// Gets the ordered non-interactive lines.
    /// </summary>
    public IReadOnlyList<EditorGizmoLine> lines => m_lines;

    /// <summary>
    /// Appends a selectable scene icon to the current gizmo frame.
    /// </summary>
    /// <param name="owner">
    /// The object that owns the resulting lifetime and state.
    /// </param>
    /// <param name="position">
    /// The position consumed by icon; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="glyph">
    /// The glyph text validated by the icon operation.
    /// </param>
    /// <param name="color">
    /// The color consumed by icon; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public void Icon(Identity owner, Vector3 position, string glyph, Color color)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(glyph);
        m_icons.Add(new EditorGizmoIcon(owner, position, glyph, color));
    }

    /// <summary>
    /// Appends a world space line to the current gizmo frame.
    /// </summary>
    /// <param name="start">
    /// The start consumed by line; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="end">
    /// The end consumed by line; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="color">
    /// The color consumed by line; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public void Line(Vector3 start, Vector3 end, Color color)
        => m_lines.Add(new EditorGizmoLine(start, end, color));
}
