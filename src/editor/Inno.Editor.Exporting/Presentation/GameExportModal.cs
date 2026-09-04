using System;
using System.Numerics;
using Inno.Build;
using Inno.Editor.Core;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Native.ImGui;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Exporting;

[EditorModal("export.game", "Export as Game", order: 310)]
internal sealed class GameExportModal(ExportWindowModule window) : EditorModal
{
    private const nuint C_TEXT_CAPACITY = 4096;

    /// <summary>
    /// Gets whether this implementation is visible.
    /// </summary>
    public override bool isVisible => window.isGameVisible;

    /// <summary>
    /// Gets whether this implementation can move.
    /// </summary>
    public override bool canMove => true;

    /// <summary>
    /// Gets the preferred initial window size in logical editor units.
    /// </summary>
    public override Vector2 initialSize => new(720f, 0f);

    /// <summary>
    /// Draws this feature using the current editor presentation context.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnDraw(EditorContext context)
    {
        EditorWidget.WrappedText(
            "Builds a self-contained Player from a fresh runtime script generation. Only imported, " +
            "content-addressed artifacts are deployed; project source files are never copied.");
        NativeImGui.Spacing();
        bool enabled = !window.isGameBusy;
        NativeImGui.BeginDisabled(!enabled);
        try
        {
            NativeImGui.TextUnformatted("Application ID");
            NativeImGui.TextDisabled($"{window.gameApplicationId} (from Project ID)");
            DrawText("Product Name", "game_name", window.gameProductName, value => window.gameProductName = value);
            DrawText("Startup Scene", "game_scene", window.gameStartupScene, value => window.gameStartupScene = value);
            DrawWindowSize();
            DrawTarget();
            DrawText("Output Directory", "game_output", window.gameOutputDirectory, value => window.gameOutputDirectory = value);
            NativeImGui.TextDisabled(
                "Identity comes from Settings > Project > Identity. Other defaults come from Build > Game.");
        }
        finally
        {
            NativeImGui.EndDisabled();
        }
        DrawOutcome();
        DrawButtons();
    }

    private void DrawWindowSize()
    {
        int width = window.gameWindowWidth;
        int height = window.gameWindowHeight;
        NativeImGui.TextUnformatted("Initial Window Width");
        NativeImGui.SetNextItemWidth(-1f);
        if (NativeImGui.InputInt("##game_window_width", ref width))
            window.gameWindowWidth = Math.Max(1, width);
        NativeImGui.TextUnformatted("Initial Window Height");
        NativeImGui.SetNextItemWidth(-1f);
        if (NativeImGui.InputInt("##game_window_height", ref height))
            window.gameWindowHeight = Math.Max(1, height);
    }

    private void DrawTarget()
    {
        NativeImGui.TextUnformatted("Target");
        NativeImGui.SetNextItemWidth(-1f);
        string preview = GetTargetLabel(window.gameTarget);
        if (!NativeImGui.BeginCombo("##game_target", preview))
            return;
        try
        {
            DrawTargetChoice(BuildTargetId.macOSArm64);
            DrawTargetChoice(BuildTargetId.windowsX64);
        }
        finally
        {
            NativeImGui.EndCombo();
        }
    }

    private void DrawTargetChoice(BuildTargetId target)
    {
        bool selected = window.gameTarget == target;
        if (NativeImGui.Selectable(GetTargetLabel(target), selected))
            window.gameTarget = target;
        if (selected)
            NativeImGui.SetItemDefaultFocus();
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
        string primaryLabel = window.isGameBusy ? "Cancel" : "Export";
        ImGuiStylePtr style = NativeImGui.GetStyle();
        float closeWidth = NativeImGui.CalcTextSize("Close").X + style.FramePadding.X * 2f;
        float primaryWidth = NativeImGui.CalcTextSize(primaryLabel).X + style.FramePadding.X * 2f;
        NativeImGui.SetCursorPosX(
            NativeImGui.GetCursorPosX() +
            MathF.Max(0f, NativeImGui.GetContentRegionAvail().X - closeWidth - style.ItemSpacing.X - primaryWidth));
        NativeImGui.BeginDisabled(window.isGameBusy);
        try
        {
            if (NativeImGui.Button("Close"))
                window.CloseGame();
        }
        finally
        {
            NativeImGui.EndDisabled();
        }
        NativeImGui.SameLine();
        if (NativeImGui.Button(primaryLabel))
        {
            if (window.isGameBusy)
                window.CancelGameExport();
            else
                window.BeginGameExport();
        }
    }

    private static string GetTargetLabel(BuildTargetId target)
        => target == BuildTargetId.macOSArm64 ? "macOS (Apple silicon)" : "Windows (x64)";

    private static void DrawText(string label, string id, string value, Action<string> apply)
    {
        NativeImGui.TextUnformatted(label);
        NativeImGui.SetNextItemWidth(-1f);
        _ = NativeImGui.InputText($"##{id}", ref value, C_TEXT_CAPACITY);
        apply(value);
    }
}
