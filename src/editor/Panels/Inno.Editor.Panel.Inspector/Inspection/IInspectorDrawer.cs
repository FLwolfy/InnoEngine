namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Draws the complete inspector for one selected target.
/// </summary>
public interface IInspectorDrawer
{
    /// <summary>
    /// Draws the selected target.
    /// </summary>
    /// <param name="context">Inspector drawing context.</param>
    void Draw(InspectorDrawContext context);
}
