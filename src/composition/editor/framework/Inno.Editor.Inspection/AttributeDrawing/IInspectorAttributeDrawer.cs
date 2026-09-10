namespace Inno.Editor.Inspection;

/// <summary>
/// Extends serialized-property presentation with reusable attribute-driven behavior.
/// </summary>
public interface IInspectorAttributeDrawer
{
    /// <summary>
    /// Updates visibility, interactivity, labeling, help, or numeric constraints before layout.
    /// </summary>
    /// <param name="context">
    /// Mutable presentation state for the annotated property.
    /// </param>
    void Update(InspectorAttributeDrawContext context)
    {
    }

    /// <summary>
    /// Draws a decorator immediately before the property row.
    /// </summary>
    /// <param name="context">
    /// Current presentation state for the annotated property.
    /// </param>
    void DrawBefore(InspectorAttributeDrawContext context)
    {
    }

    /// <summary>
    /// Draws a decorator immediately after the property row.
    /// </summary>
    /// <param name="context">
    /// Current presentation state for the annotated property.
    /// </param>
    void DrawAfter(InspectorAttributeDrawContext context)
    {
    }
}
