using System;
using System.Linq;
using System.Numerics;
using Inno.Editor.Core;
using Inno.Editor.Rendering;
using Inno.Engine.Scene;
using Inno.Rendering;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.GameView;

/// <summary>Shows the first active runtime Camera through the current pipeline configuration.</summary>
[EditorPanel("rendering.game-view", "Game", order: 220)]
internal sealed class GameViewPanel : EditorPanel
{
    private const string C_VIEWPORT_ID = "game-view";

    private readonly EditorRenderingModule m_rendering;

    /// <inheritdoc />
    public override bool useWindowPadding => false;

    /// <summary>Creates the Game View panel.</summary>
    internal GameViewPanel(EditorRenderingModule rendering)
    {
        m_rendering = rendering ?? throw new ArgumentNullException(nameof(rendering));
    }

    /// <inheritdoc />
    protected override void OnDraw(EditorContext context)
    {
        _ = context;
        Camera? camera = FindCamera();
        if (camera is null)
        {
            m_rendering.Release(C_VIEWPORT_ID);
            NativeImGui.TextUnformatted("No active runtime Camera is loaded.");
            return;
        }

        Vector2 available = NativeImGui.GetContentRegionAvail();
        int width = Math.Max(1, (int)MathF.Floor(available.X));
        int height = Math.Max(1, (int)MathF.Floor(available.Y));
        RenderRequest cameraRequest = camera.CreateRenderRequest(width, height);
        EditorViewportOutput output = m_rendering.Submit(new EditorViewportRequest(
            C_VIEWPORT_ID,
            cameraRequest.view,
            cameraRequest.renderPath,
            cameraRequest.clearMode,
            cameraRequest.backgroundColor,
            cameraRequest.priority));
        if (output.isReady)
        {
            m_rendering.Draw(output, new Vector2(width, height));
        }
        else
        {
            NativeImGui.TextUnformatted("Preparing Game View GPU target...");
        }
    }

    /// <inheritdoc />
    protected override void OnDetach(EditorContext context)
    {
        _ = context;
        m_rendering.Release(C_VIEWPORT_ID);
    }

    private static Camera? FindCamera()
        => SceneManager.loadedScenes
            .SelectMany(static scene => scene.GetObjects())
            .Where(static gameObject => gameObject.activeInHierarchy)
            .Select(static gameObject => gameObject.TryGetComponent(out Camera? camera) ? camera : null)
            .FirstOrDefault(static camera => camera is not null);
}
