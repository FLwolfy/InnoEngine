using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Editor.Core;
using Inno.Editor.Rendering;
using Inno.Editor.Scene;
using Inno.Editor.Settings;
using Inno.Scene;
using Inno.Rendering;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.GameView;

/// <summary>
/// Presents the active Plugin provider for the open Game viewport purpose.
/// </summary>
[EditorPanel("rendering.game-view", "Game", order: 220, menuPath: "Viewports")]
internal sealed class GameViewPanel : EditorPanel
{
    private const string C_VIEWPORT_ID = "game-view";
    private static readonly EditorViewportKindId S_KIND = new("inno.editor.viewport.game");

    private readonly EditorRenderingModule m_rendering;
    private readonly IEditorGameScenePresentation m_scenePresentation;
    private readonly EditorSettings m_settings;
    private Vector4 m_backgroundColor;

    internal GameViewPanel(
        EditorRenderingModule rendering,
        IEditorGameScenePresentation scenePresentation,
        EditorSettings settings)
    {
        m_rendering = rendering ?? throw new ArgumentNullException(nameof(rendering));
        m_scenePresentation = scenePresentation ?? throw new ArgumentNullException(nameof(scenePresentation));
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// Gets whether use window padding is enabled for this implementation.
    /// </summary>
    public override bool useWindowPadding => false;

    /// <summary>
    /// Gets whether allow scrolling is enabled for this implementation.
    /// </summary>
    public override bool allowScrolling => false;

    /// <summary>
    /// Draws this feature using the current editor presentation context.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
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

    /// <summary>
    /// Attaches this feature to its owning runtime generation.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnAttach(EditorContext context)
    {
        _ = context;
        ApplySettings(m_settings);
        m_settings.changed += ApplySettings;
    }

    /// <summary>
    /// Detaches this feature and releases generation-scoped state.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
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
        EditorScenePresentationSnapshot presentation = m_scenePresentation.Capture();
        var contents = new List<RenderContentReference>(presentation.scenes.Count);
        RenderContentId? activeContent = null;
        foreach (GameScene scene in presentation.scenes)
        {
            var contentId = new RenderContentId(scene.identity.persistentId);
            contents.Add(new RenderContentReference(contentId, scene));
            if (ReferenceEquals(scene, presentation.activeScene))
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
