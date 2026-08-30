using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Editor.Core;
using Inno.Editor.Rendering;
using Inno.Editor.Scene;
using Inno.Editor.Settings;
using Inno.Engine.Scene;
using Inno.Rendering;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.GameView;

/// <summary>Presents the active Plugin provider for the open Game viewport purpose.</summary>
[EditorPanel("rendering.game-view", "Game", order: 220)]
internal sealed class GameViewPanel : EditorPanel
{
    private const string C_VIEWPORT_ID = "game-view";
    private static readonly EditorViewportKindId S_KIND = new("inno.editor.viewport.game");

    private readonly EditorRenderingModule m_rendering;
    private readonly IEditorSceneWorkspace m_workspace;
    private readonly EditorSettings m_settings;
    private Vector4 m_backgroundColor;

    internal GameViewPanel(
        EditorRenderingModule rendering,
        IEditorSceneWorkspace workspace,
        EditorSettings settings)
    {
        m_rendering = rendering ?? throw new ArgumentNullException(nameof(rendering));
        m_workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
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
        m_rendering.SetPresentation(
            C_VIEWPORT_ID,
            new EditorViewportPresentation(new Inno.Core.Mathematics.Color(
                m_backgroundColor.X,
                m_backgroundColor.Y,
                m_backgroundColor.Z,
                m_backgroundColor.W)));
        m_rendering.SetContentScope(C_VIEWPORT_ID, CreateContentScope());
        if (!m_rendering.TrySubmit(S_KIND, C_VIEWPORT_ID, width, height, out EditorViewportOutput output))
        {
            DrawUnavailable(
                available,
                m_rendering.GetProviderError(S_KIND) ?? "No active rendering provider for Game View.");
            return;
        }
        if (!output.isReady)
        {
            DrawUnavailable(available, "Preparing Game View GPU target...");
            return;
        }
        m_rendering.Draw(output, new Vector2(width, height));
    }

    /// <inheritdoc />
    protected override void OnAttach(EditorContext context)
    {
        _ = context;
        ApplySettings(m_settings);
        m_settings.changed += ApplySettings;
    }

    /// <inheritdoc />
    protected override void OnDetach(EditorContext context)
    {
        _ = context;
        m_settings.changed -= ApplySettings;
        m_rendering.Release(C_VIEWPORT_ID);
    }

    private void ApplySettings(EditorSettings settings)
        => m_backgroundColor = GameViewBackgroundSetting.Read(settings);

    private RenderContentScope CreateContentScope()
    {
        var contents = new List<RenderContentReference>(m_workspace.scenes.Count);
        RenderContentId? activeContent = null;
        foreach (GameScene scene in m_workspace.scenes)
        {
            if (scene.isDestroyed)
                continue;
            var contentId = new RenderContentId(scene.identity.persistentId);
            contents.Add(new RenderContentReference(contentId, scene));
            if (ReferenceEquals(scene, m_workspace.activeScene))
                activeContent = contentId;
        }
        return new RenderContentScope(contents, activeContent);
    }

    private void DrawUnavailable(Vector2 size, string message)
    {
        Vector2 minimum = NativeImGui.GetCursorScreenPos();
        NativeImGui.GetWindowDrawList().AddRectFilled(
            minimum,
            minimum + size,
            NativeImGui.ColorConvertFloat4ToU32(m_backgroundColor));
        NativeImGui.TextUnformatted(message);
    }
}
