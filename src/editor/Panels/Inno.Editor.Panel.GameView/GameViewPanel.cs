using System;
using System.Numerics;

using Inno.Editor.Core;
using Inno.Editor.Rendering;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.GameView;

/// <summary>Presents the active Plugin provider for the open Game viewport purpose.</summary>
[EditorPanel("rendering.game-view", "Game", order: 220)]
internal sealed class GameViewPanel : EditorPanel
{
    private const string C_VIEWPORT_ID = "game-view";
    private static readonly EditorViewportKindId S_KIND = new("inno.editor.viewport.game");

    private readonly EditorRenderingModule m_rendering;

    internal GameViewPanel(EditorRenderingModule rendering)
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
                m_rendering.GetProviderError(S_KIND) ?? "No active rendering provider for Game View.");
            return;
        }
        if (!output.isReady)
        {
            NativeImGui.TextUnformatted("Preparing Game View GPU target...");
            return;
        }
        m_rendering.Draw(output, new Vector2(width, height));
    }

    /// <inheritdoc />
    protected override void OnDetach(EditorContext context)
    {
        _ = context;
        m_rendering.Release(C_VIEWPORT_ID);
    }
}
