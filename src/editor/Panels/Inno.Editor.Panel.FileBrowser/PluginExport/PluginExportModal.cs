using System;
using System.Numerics;

using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Native.ImGui;

using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.FileBrowser;

/// <summary>Draws the blocking Plugin export destination window.</summary>
[EditorModal("asset-browser.plugin-export", "Export Plugin", order: 110)]
internal sealed class PluginExportModal(PluginExportWindowModule export) : EditorModal
{
    private const uint C_PATH_CAPACITY = 4096;

    /// <inheritdoc />
    public override bool isVisible => export.isVisible;

    /// <inheritdoc />
    public override bool canMove => true;

    /// <inheritdoc />
    public override Vector2 initialSize => new(720f, 0f);

    /// <inheritdoc />
    protected override void OnDraw(EditorContext context)
    {
        string kind = export.kind == PluginExportKind.Zip ? "ZIP" : "Folder";
        EditorWidget.WrappedText(
            $"Export '{export.pluginId}' as a read-only {kind} installation. " +
            "The result is not installed into this authoring project automatically.");
        NativeImGui.Spacing();
        NativeImGui.TextUnformatted("Destination");
        string outputPath = export.outputPath;
        NativeImGui.SetNextItemWidth(-1f);
        if (NativeImGui.InputText(
                "##plugin_export_destination",
                ref outputPath,
                C_PATH_CAPACITY,
                ImGuiInputTextFlags.EnterReturnsTrue))
        {
            export.outputPath = outputPath;
            export.Export();
            return;
        }
        export.outputPath = outputPath;

        if (!string.IsNullOrEmpty(export.error))
        {
            NativeImGui.Spacing();
            NativeImGui.PushStyleColor(ImGuiCol.Text, EditorPalette.error);
            try
            {
                EditorWidget.WrappedText(export.error);
            }
            finally
            {
                NativeImGui.PopStyleColor();
            }
        }

        NativeImGui.Spacing();
        DrawButtons();
    }

    private void DrawButtons()
    {
        ImGuiStylePtr style = NativeImGui.GetStyle();
        float cancelWidth = NativeImGui.CalcTextSize("Cancel").X + style.FramePadding.X * 2f;
        float exportWidth = NativeImGui.CalcTextSize("Export").X + style.FramePadding.X * 2f;
        float totalWidth = cancelWidth + style.ItemSpacing.X + exportWidth;
        NativeImGui.SetCursorPosX(
            NativeImGui.GetCursorPosX() +
            MathF.Max(0f, NativeImGui.GetContentRegionAvail().X - totalWidth));
        if (NativeImGui.Button("Cancel"))
            export.Close();
        NativeImGui.SameLine();
        if (NativeImGui.Button("Export"))
            export.Export();
    }
}
