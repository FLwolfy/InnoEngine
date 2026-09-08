#nullable disable

namespace Inno.Native.ImGui
{
    using BGCS.Runtime;

    /// <summary>
    /// Provides the generated native Dear ImGui ABI surface used exclusively by the platform adapter.
    /// </summary>
public static unsafe partial class ImGuiP
    {
        internal static FunctionTable funcTable;

        static ImGuiP()
        {
            funcTable = ImGui.funcTable;
        }
    }
}