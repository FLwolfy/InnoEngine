using System;

using Inno.Assets.File;
using Inno.Editor.Core;
using Inno.Editor.Inspection;
using Inno.Editor.Interactions;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Platform.ImGui;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.FileBrowser;

[InspectionDrawer(typeof(AssetFileEntry))]
internal sealed class AssetSelectionInspectionDrawer : InspectionDrawer<AssetFileEntry>
{
    private readonly IInspectionIconProvider<AssetFileEntry> m_icons;

    /// <summary>
    /// Creates an Asset source drawer that shares the Asset Browser presentation registry.
    /// </summary>
    /// <param name="icons">The Asset Browser presentation provider used to resolve the source icon.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="icons"/> is <see langword="null"/>.
    /// </exception>
    internal AssetSelectionInspectionDrawer(IInspectionIconProvider<AssetFileEntry> icons)
    {
        m_icons = icons ?? throw new ArgumentNullException(nameof(icons));
    }

    public override string icon => ImGuiIcon.File;

    protected override string GetIcon(InspectionDrawContext context, AssetFileEntry target)
        => m_icons.GetIcon(target);

    protected override (string name, Action<string>? setter) BindName(
        InspectionDrawContext context,
        AssetFileEntry target)
        => (target.nameWithoutExtension, null);

    protected override void DrawHeader(InspectionDrawContext context, AssetFileEntry target)
        => EditorWidget.ColoredText(EditorPalette.assetBreadcrumbText, target.assetPath.ToString());

    protected override void Draw(InspectionDrawContext context, AssetFileEntry entry)
    {
        DrawMetadata("Type", entry.isDirectory ? "Directory" : "File");
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
