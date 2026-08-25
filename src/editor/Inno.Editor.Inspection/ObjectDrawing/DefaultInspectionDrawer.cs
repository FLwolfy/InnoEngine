using System;

using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Inspection;

[InspectionDrawer(typeof(object), useForChildren: true, priority: int.MinValue)]
internal sealed class DefaultInspectionDrawer : InspectionDrawer<object>
{
    public override string icon => ImGuiIcon.CircleInfo;

    protected override (string name, Action<string>? setter) BindName(
        InspectionDrawContext context,
        object target)
        => (target.GetType().Name, null);

    protected override void DrawHeader(InspectionDrawContext context, object target)
        => NativeImGui.TextUnformatted(target.GetType().FullName ?? target.GetType().Name);

    protected override void Draw(InspectionDrawContext context, object target)
    {
    }
}
