using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.Rendering;
using Inno.Editor.Scene;
using Inno.Editor.Settings;
using Inno.Scene;
using Inno.Rendering;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;

namespace Inno.Editor.Panel.GameView;

/// <summary>
/// Presents the active Plugin provider for the open Game viewport purpose.
/// </summary>
[EditorPanel("rendering.game-view", "Game", order: 220, menuPath: "Viewports")]
internal sealed class GameViewPanel : EditorPanel
{
    private const string C_VIEWPORT_ID = "game-view";
    private static readonly EditorViewportKindId S_KIND = new("inno.editor.viewport.game");
    private static readonly Vector2 S_UNAVAILABLE_PADDING = new(48f, 32f);

    private readonly EditorRenderingModule m_rendering;
    private readonly IEditorGameScenePresentation m_scenePresentation;
    private readonly EditorSettings m_settings;
    private Vector4 m_backgroundColor;
    private GameViewFraming m_framing;

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
        if (available.X <= 0f || available.Y <= 0f)
            return;
        GameViewportLayout layout = CalculateLayout(available, m_framing);
        m_rendering.SetPresentation(
            C_VIEWPORT_ID,
            new EditorViewportPresentation(new Inno.Core.Mathematics.Color(
                m_backgroundColor.X,
                m_backgroundColor.Y,
                m_backgroundColor.Z,
                m_backgroundColor.W)));
        m_rendering.SetContentScope(C_VIEWPORT_ID, CreateContentScope());
        if (!m_rendering.TrySubmit(
                S_KIND,
                C_VIEWPORT_ID,
                layout.pixelWidth,
                layout.pixelHeight,
                out EditorViewportOutput output))
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

        Vector2 origin = NativeImGui.GetCursorScreenPos();
        if (m_framing.preserveAspectRatio)
        {
            NativeImGui.GetWindowDrawList().AddRectFilled(
                origin,
                origin + available,
                NativeImGui.ColorConvertFloat4ToU32(Vector4.Zero));
        }
        NativeImGui.SetCursorScreenPos(origin + layout.offset);
        m_rendering.Draw(output, layout.size);
        NativeImGui.SetCursorScreenPos(origin);
        NativeImGui.Dummy(available);
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
    {
        m_backgroundColor = GameViewBackgroundSetting.Read(settings);
        m_framing = GameViewFramingSetting.Read(settings);
    }

    private static GameViewportLayout CalculateLayout(Vector2 available, GameViewFraming framing)
    {
        int availableWidth = Math.Max(1, (int)MathF.Floor(available.X));
        int availableHeight = Math.Max(1, (int)MathF.Floor(available.Y));
        if (!framing.preserveAspectRatio)
        {
            var fullSize = new Vector2(availableWidth, availableHeight);
            return new GameViewportLayout(Vector2.Zero, fullSize, availableWidth, availableHeight);
        }

        float targetAspect = framing.aspectWidth / (float)framing.aspectHeight;
        int width = availableWidth;
        int height = Math.Max(1, (int)MathF.Floor(width / targetAspect));
        if (height > availableHeight)
        {
            height = availableHeight;
            width = Math.Max(1, (int)MathF.Floor(height * targetAspect));
        }

        var size = new Vector2(width, height);
        return new GameViewportLayout((available - size) * 0.5f, size, width, height);
    }

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
        NativeImGui.PushStyleColor(ImGuiCol.Text, EditorPalette.textDisabled);
        try
        {
            EditorWidget.CenteredWrappedText(message, size, S_UNAVAILABLE_PADDING);
        }
        finally
        {
            NativeImGui.PopStyleColor();
        }
    }

    private readonly record struct GameViewportLayout(
        Vector2 offset,
        Vector2 size,
        int pixelWidth,
        int pixelHeight);
}
