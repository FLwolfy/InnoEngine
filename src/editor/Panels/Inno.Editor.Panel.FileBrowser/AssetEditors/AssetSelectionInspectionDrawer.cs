using System;

using Inno.Assets.Pipeline;
using Inno.Editor.Core;
using Inno.Editor.Inspection;
using Inno.Editor.Interactions;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Platform.Sdl3.ImGui;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.FileBrowser;

[InspectionDrawer(typeof(AssetFileEntry))]
internal sealed class AssetSelectionInspectionDrawer : InspectionDrawer<AssetFileEntry>
{
    private readonly AssetEditorModule m_assets;

    /// <summary>
    /// Creates an Asset source drawer that shares the Asset Browser presentation registry.
    /// </summary>
    /// <param name="assets">
    /// The Asset Browser owner used to resolve source icons and authoritative Plugin ownership.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="assets"/> is <see langword="null"/>.
    /// </exception>
    internal AssetSelectionInspectionDrawer(AssetEditorModule assets)
    {
        m_assets = assets ?? throw new ArgumentNullException(nameof(assets));
    }

    /// <summary>
    /// Gets the icon glyph used to represent this item in the editor.
    /// </summary>
    public override string icon => ImGuiIcon.File;

    /// <summary>
    /// Retrieves the requested icon value from current authoritative state.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
    /// <returns>
    /// The validated text representation owned by the caller.
    /// </returns>
    protected override string GetIcon(InspectionDrawContext context, AssetFileEntry target)
        => m_assets.GetIcon(target);

    /// <summary>
    /// Binds a caller-visible label to the current inspection target.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
    /// <returns>
    /// The validated (string name, actionstring? setter) that represents the completed operation.
    /// </returns>
    protected override (string name, Action<string>? setter) BindName(
        InspectionDrawContext context,
        AssetFileEntry target)
        => (target.nameWithoutExtension, null);

    /// <summary>
    /// Renders the header presentation for the current editor frame.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
    protected override void DrawHeader(InspectionDrawContext context, AssetFileEntry target)
        => EditorWidget.ColoredText(EditorPalette.assetBreadcrumbText, target.assetPath.ToString());

    /// <summary>
    /// Renders the value presentation for the current editor frame.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="entry">
    /// The entry consumed by draw; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    protected override void Draw(InspectionDrawContext context, AssetFileEntry entry)
    {
        DrawMetadata(
            "Type",
            m_assets.IsPluginRoot(entry)
                ? "IPlugin"
                : entry.isDirectory && AssetSample.HasSampleDirectoryName(entry.assetPath)
                    ? "ISample"
                : entry.isDirectory
                    ? "Directory"
                    : "File");
        if (!entry.isDirectory)
        {
            DrawMetadata("Extension", string.IsNullOrEmpty(entry.extension) ? "<none>" : entry.extension);
        }
    }

    private static void DrawMetadata(string label, string value)
    {
        NativeImGui.TextUnformatted(label);
        NativeImGui.Separator();
        NativeImGui.TextUnformatted(value);
        NativeImGui.Spacing();
    }
}
