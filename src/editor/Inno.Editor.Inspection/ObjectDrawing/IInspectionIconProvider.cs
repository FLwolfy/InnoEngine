namespace Inno.Editor.Inspection;

/// <summary>
/// Resolves a presentation icon for inspected targets owned by another editor feature.
/// </summary>
/// <typeparam name="TTarget">The target type whose icon can be resolved.</typeparam>
public interface IInspectionIconProvider<in TTarget>
{
    /// <summary>
    /// Resolves the presentation icon for one inspected target.
    /// </summary>
    /// <param name="target">The target whose icon should be resolved.</param>
    /// <returns>The icon glyph registered by the owning feature.</returns>
    string GetIcon(TTarget target);
}
