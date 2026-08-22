using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

[InspectorDrawer(typeof(object), useForChildren: true, priority: int.MinValue)]
internal sealed class DefaultInspectorDrawer : InspectorDrawer<object>
{
    public override string icon => ImGuiIcon.CircleInfo;

    protected override string GetName(InspectorDrawContext context, object target)
        => target.GetType().Name;

    protected override void DrawHeader(InspectorDrawContext context, object target)
        => NativeImGui.TextUnformatted(target.GetType().FullName ?? target.GetType().Name);

    protected override void Draw(InspectorDrawContext context, object target)
    {
    }
}
