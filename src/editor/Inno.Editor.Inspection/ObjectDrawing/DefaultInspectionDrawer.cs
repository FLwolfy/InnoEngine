using System;

using Inno.Platform.Sdl3.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Inspection;

[InspectionDrawer(typeof(object), useForChildren: true, priority: int.MinValue)]
internal sealed class DefaultInspectionDrawer : InspectionDrawer<object>
{
    /// <summary>
    /// Gets the icon glyph used to represent this item in the editor.
    /// </summary>
    public override string icon => ImGuiIcon.CircleInfo;

    /// <summary>
    /// Binds a caller-visible label to the current inspection target.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
    /// <returns>
    /// The validated (string name, actionstring? setter) that represents the completed operation.
    /// </returns>
    protected override (string name, Action<string>? setter) BindName(
        InspectionDrawContext context,
        object target)
        => (target.GetType().Name, null);

    /// <summary>
    /// Renders the header presentation for the current editor frame.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
    protected override void DrawHeader(InspectionDrawContext context, object target)
        => NativeImGui.TextUnformatted(target.GetType().FullName ?? target.GetType().Name);

    /// <summary>
    /// Renders the value presentation for the current editor frame.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
    protected override void Draw(InspectionDrawContext context, object target)
    {
    }
}
