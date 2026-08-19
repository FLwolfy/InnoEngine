namespace Inno.Editor.Scene.Inspection;

/// <summary>
/// Draws and optionally edits one serialized property value.
/// </summary>
public interface IPropertyDrawer
{
    /// <summary>
    /// Draws a property through its encapsulated value accessors.
    /// </summary>
    /// <param name="context">Property drawing context.</param>
    void Draw(PropertyDrawContext context);
}
