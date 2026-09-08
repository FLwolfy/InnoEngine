using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Logging;

internal static class ConsoleEntryImGuiIdentity
{
    internal static void Push(string identity)
    {
        NativeImGui.PushID(identity);
    }

    internal static void Pop()
    {
        NativeImGui.PopID();
    }
}
