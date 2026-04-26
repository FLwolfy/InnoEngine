using Inno.Core.Logging;
using Inno.Editor.Core;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panels;

/// <summary>
/// Displays logs from <see cref="EditorLogBuffer"/>.
/// </summary>
public sealed class LogPanel : EditorPanel
{
    /// <summary>
    /// Creates the panel.
    /// </summary>
    public LogPanel()
        : base("log.console", "Log")
    {
    }

    /// <inheritdoc />
    public override void OnRender(EditorContext context)
    {
        LogEntry[] entries = context.logs.Snapshot();
        if (entries.Length == 0)
        {
            NativeImGui.TextUnformatted("No logs yet.");
            return;
        }

        for (int i = 0; i < entries.Length; i++)
        {
            LogEntry entry = entries[i];
            NativeImGui.TextUnformatted($"{entry.level}: {entry.message}");
        }
    }
}
