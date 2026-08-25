using System;

using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.Settings;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;

namespace Inno.Editor.Panel.Global;

/// <summary>
/// Applies the configured actual UI size and manages transient zoom multiples around it.
/// </summary>
[EditorModule(order: 10)]
internal sealed class EditorZoomModule(EditorSettings settings) : EditorModule
{
    private const string C_ACTUAL_SIZE_PATH = "Global/Appearance/Accessibility/Actual Size";

    private float m_actualSize = 1f;
    private int m_zoomStep;

    internal float zoom => EditorWidget.style.zoom;

    internal bool canZoomIn => ResolveZoom(m_zoomStep + 1) > zoom + 0.0001f;

    internal bool canZoomOut => ResolveZoom(m_zoomStep - 1) < zoom - 0.0001f;

    internal bool isActualSize => m_zoomStep == 0;

    internal bool ZoomIn()
        => SetStep(m_zoomStep + 1);

    internal bool ZoomOut()
        => SetStep(m_zoomStep - 1);

    internal bool UseActualSize()
        => SetStep(0);

    /// <inheritdoc />
    protected override void OnStart(EditorContext context)
    {
        ApplyActualSize(settings);
        settings.changed += ApplyActualSize;
    }

    /// <inheritdoc />
    protected override void OnStop(EditorContext context)
    {
        settings.changed -= ApplyActualSize;
    }

    private void ApplyActualSize(EditorSettings changedSettings)
    {
        m_actualSize = NormalizeActualSize(
            changedSettings.Get(C_ACTUAL_SIZE_PATH).GetAsSingle("value", 1f));
        m_zoomStep = 0;
        _ = EditorWidget.style.SetZoom(m_actualSize);
    }

    private float ResolveZoom(int step)
    {
        float multiplier = 1f + step * EditorStyleMetrics.C_ZOOM_STEP;
        return Math.Clamp(
            m_actualSize * multiplier,
            EditorStyleMetrics.C_MIN_ZOOM,
            EditorStyleMetrics.C_MAX_ZOOM);
    }

    private bool SetStep(int step)
    {
        float resolved = ResolveZoom(step);
        if (MathF.Abs(resolved - zoom) < 0.0001f)
            return false;
        m_zoomStep = step;
        return EditorWidget.style.SetZoom(resolved);
    }

    private static float NormalizeActualSize(float value)
        => float.IsFinite(value)
            ? Math.Clamp(value, EditorStyleMetrics.C_MIN_ZOOM, EditorStyleMetrics.C_MAX_ZOOM)
            : 1f;
}
