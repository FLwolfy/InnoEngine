using System;
using System.Numerics;
using Inno.Editor.Core;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Native.ImGui;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Exporting;

[EditorModal("export.plugin", "Export as Plugin", order: 300)]
internal sealed class PluginExportModal(ExportWindowModule window) : EditorModal
{
    private const nuint C_TEXT_CAPACITY = 4096;

    /// <summary>
    /// Gets whether this implementation is visible.
    /// </summary>
    public override bool isVisible => window.isPluginVisible;

    /// <summary>
    /// Gets whether this implementation can move.
    /// </summary>
    public override bool canMove => true;

    /// <summary>
    /// Gets the preferred initial window size in logical editor units.
    /// </summary>
    public override Vector2 initialSize => new(680f, 0f);

    /// <summary>
    /// Draws this feature using the current editor presentation context.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnDraw(EditorContext context)
    {
        EditorWidget.WrappedText(
            "Packages the current project's complete authored source. Plugin.inno, dependency declarations, " +
            "settings contributions, and deterministic metadata are generated automatically.");
        NativeImGui.Spacing();
        NativeImGui.BeginDisabled(window.isPluginBusy);
        try
        {
            DrawText("Plugin ID", "plugin_id", window.pluginId, value => window.pluginId = value);
            DrawText("Display Name", "plugin_name", window.pluginDisplayName, value => window.pluginDisplayName = value);
            DrawText("Destination IPlugin", "plugin_output", window.pluginOutputPath, value => window.pluginOutputPath = value);
        }
        finally
        {
            NativeImGui.EndDisabled();
        }
        NativeImGui.TextDisabled(
            window.includePluginDependencies
                ? "Dependency packages: embedded (Editor > Export > Plugin)."
                : "Dependency packages: declared only (Editor > Export > Plugin)."
        );
        DrawOutcome();
        DrawButtons();
    }

    private void DrawOutcome()
    {
        if (!string.IsNullOrEmpty(window.error))
        {
            NativeImGui.Spacing();
            EditorWidget.WrappedText(window.error);
        }
        else if (!string.IsNullOrEmpty(window.status))
        {
            NativeImGui.Spacing();
            EditorWidget.WrappedText(window.status);
        }
    }

    private void DrawButtons()
    {
        NativeImGui.Spacing();
        ImGuiStylePtr style = NativeImGui.GetStyle();
        float closeWidth = NativeImGui.CalcTextSize("Close").X + style.FramePadding.X * 2f;
        string primaryLabel = window.isPluginBusy ? "Cancel" : "Export";
        float exportWidth = NativeImGui.CalcTextSize(primaryLabel).X + style.FramePadding.X * 2f;
        NativeImGui.SetCursorPosX(
            NativeImGui.GetCursorPosX() +
            MathF.Max(0f, NativeImGui.GetContentRegionAvail().X - closeWidth - style.ItemSpacing.X - exportWidth));
        NativeImGui.BeginDisabled(window.isPluginBusy);
        try
        {
            if (NativeImGui.Button("Close"))
                window.ClosePlugin();
        }
        finally
        {
            NativeImGui.EndDisabled();
        }
        NativeImGui.SameLine();
        if (NativeImGui.Button(primaryLabel))
        {
            if (window.isPluginBusy)
                window.CancelPluginExport();
            else
                window.BeginPluginExport();
        }
    }

    private static void DrawText(string label, string id, string value, Action<string> apply)
    {
        NativeImGui.TextUnformatted(label);
        NativeImGui.SetNextItemWidth(-1f);
        if (NativeImGui.InputText($"##{id}", ref value, C_TEXT_CAPACITY))
            apply(value);
        else
            apply(value);
    }
}
