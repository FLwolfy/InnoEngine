using System.Numerics;

using Inno.Editor.Core;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Scripting;

/// <summary>Displays real script compiler progress while blocking editor interaction.</summary>
[EditorModal("scripting.compilation", "Compiling Scripts", order: 100)]
public sealed class ScriptCompilationModal(ScriptingModule scripting) : EditorModal
{
    /// <inheritdoc />
    public override bool isVisible => scripting.isCompiling;

    /// <inheritdoc />
    public override void Draw(EditorContext context)
    {
        float progress = scripting.progress;
        NativeImGui.TextUnformatted(scripting.status);
        NativeImGui.ProgressBar(progress, new Vector2(-1f, 0f), $"{progress:P0}");
    }
}
