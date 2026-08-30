using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Logging;

internal static class ConsoleEntryImGuiIdentity
{
    internal static void Push(EditorConsoleEntryId identity)
    {
        NativeImGui.PushID((int)identity.kind);
        NativeImGui.PushID(unchecked((int)(identity.value >> 32)));
        NativeImGui.PushID(unchecked((int)identity.value));
    }

    internal static void Pop()
    {
        NativeImGui.PopID();
        NativeImGui.PopID();
        NativeImGui.PopID();
    }
}
