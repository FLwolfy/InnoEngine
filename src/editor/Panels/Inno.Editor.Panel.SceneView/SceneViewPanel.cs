using System;
using System.Numerics;

using Inno.Editor.Core;
using Inno.Editor.Rendering;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.SceneView;

/// <summary>Presents the active Plugin provider for the open Scene viewport purpose.</summary>
[EditorPanel("rendering.scene-view", "Scene", order: 210)]
internal sealed class SceneViewPanel : EditorPanel
{
    private const string C_VIEWPORT_ID = "scene-view";
    private static readonly EditorViewportKindId S_KIND = new("inno.editor.viewport.scene");

    private readonly EditorRenderingModule m_rendering;

    internal SceneViewPanel(EditorRenderingModule rendering)
    {
        m_rendering = rendering ?? throw new ArgumentNullException(nameof(rendering));
    }

    /// <inheritdoc />
    public override bool useWindowPadding => false;

    /// <inheritdoc />
    protected override void OnDraw(EditorContext context)
    {
        _ = context;
        Vector2 available = NativeImGui.GetContentRegionAvail();
        int width = Math.Max(1, (int)MathF.Floor(available.X));
        int height = Math.Max(1, (int)MathF.Floor(available.Y));
        m_rendering.DrawProviderToolbar(S_KIND, C_VIEWPORT_ID, width, height);
        if (!m_rendering.TrySubmit(S_KIND, C_VIEWPORT_ID, width, height, out EditorViewportOutput output))
        {
            NativeImGui.TextUnformatted(
                m_rendering.GetProviderError(S_KIND) ?? "No active rendering provider for Scene View.");
            return;
        }
        if (!output.isReady)
        {
            NativeImGui.TextUnformatted("Preparing Scene View GPU target...");
            return;
        }

        m_rendering.Draw(output, new Vector2(width, height));
        if (!NativeImGui.IsItemClicked(ImGuiMouseButton.Left))
            return;
        Vector2 minimum = NativeImGui.GetItemRectMin();
        Vector2 maximum = NativeImGui.GetItemRectMax();
        Vector2 mouse = NativeImGui.GetMousePos();
        float x = (mouse.X - minimum.X) / Math.Max(1f, maximum.X - minimum.X);
        float y = (mouse.Y - minimum.Y) / Math.Max(1f, maximum.Y - minimum.Y);
        m_rendering.HandlePointer(S_KIND, C_VIEWPORT_ID, width, height, x, y, button: 0);
    }

    /// <inheritdoc />
    protected override void OnDetach(EditorContext context)
    {
        _ = context;
        m_rendering.Release(C_VIEWPORT_ID);
    }
}
