#nullable disable

namespace Inno.Native.ImGui
{
    using BGCS.Runtime;

    public static unsafe partial class ImGuiP
    {
        internal static FunctionTable funcTable;

        static ImGuiP()
        {
            funcTable = ImGui.funcTable;
        }
    }
}