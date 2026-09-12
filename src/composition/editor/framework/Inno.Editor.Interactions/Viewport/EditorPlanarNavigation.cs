using System;
using System.Numerics;

namespace Inno.Editor.Interactions;

/// <summary>Shares captured planar gestures and mouse-anchored zoom math between scene and graph canvases.</summary>
public sealed class EditorPlanarNavigation
{
    private bool m_primary;

    /// <summary>Gets whether this canvas owns a pan gesture, including while the pointer is outside its bounds.</summary>
    public bool isPanning { get; private set; }

    /// <summary>Advances pointer capture using logical button state rather than window-local release events.</summary>
    /// <param name="hovered">Whether a new gesture may start in this canvas.</param>
    /// <param name="primaryPressed">Whether the primary button was pressed this frame.</param>
    /// <param name="middlePressed">Whether the middle button was pressed this frame.</param>
    /// <param name="primaryDown">Current global primary button state.</param>
    /// <param name="middleDown">Current global middle button state.</param>
    /// <param name="alt">Whether Alt is held when the gesture starts.</param>
    /// <param name="allowAltPrimary">Whether Alt-primary belongs to pan rather than another navigation mode.</param>
    /// <returns>Whether this canvas owns the current pointer gesture.</returns>
    public bool Update(bool hovered, bool primaryPressed, bool middlePressed, bool primaryDown, bool middleDown,
        bool alt, bool allowAltPrimary = true)
    {
        if (isPanning && !(m_primary ? primaryDown : middleDown)) Cancel();
        if (!isPanning && hovered && (middlePressed || allowAltPrimary && alt && primaryPressed))
        {
            m_primary = !middlePressed;
            isPanning = true;
        }
        return isPanning;
    }

    /// <summary>Releases capture when a canvas closes or a gesture is cancelled.</summary>
    public void Cancel() => isPanning = false;

    /// <summary>Converts wheel input to the common exponential magnification used by Editor canvases.</summary>
    /// <param name="wheel">Logical wheel units, independent of framebuffer DPI.</param>
    /// <param name="sensitivity">Positive exponential sensitivity.</param>
    /// <returns>A finite positive magnification factor; positive wheel magnifies content.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Input is non-finite or sensitivity is not positive.</exception>
    public static float WheelFactor(float wheel, float sensitivity = 0.16f)
    {
        if (!float.IsFinite(wheel)) throw new ArgumentOutOfRangeException(nameof(wheel));
        if (!float.IsFinite(sensitivity) || sensitivity <= 0) throw new ArgumentOutOfRangeException(nameof(sensitivity));
        return MathF.Exp(Math.Clamp(wheel * sensitivity, -20f, 20f));
    }

    /// <summary>Changes the screen-space origin so zoom preserves the same content point beneath the cursor.</summary>
    /// <param name="origin">Current logical-pixel content origin.</param>
    /// <param name="pivot">Logical-pixel cursor position relative to the canvas.</param>
    /// <param name="previousScale">Previous positive content magnification.</param>
    /// <param name="nextScale">New positive content magnification after clamping.</param>
    /// <returns>The origin preserving the pivot's content coordinate.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An input is non-finite or a scale is not positive.</exception>
    public static Vector2 ZoomOrigin(Vector2 origin, Vector2 pivot, float previousScale, float nextScale)
    {
        if (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y)) throw new ArgumentOutOfRangeException(nameof(origin));
        if (!float.IsFinite(pivot.X) || !float.IsFinite(pivot.Y)) throw new ArgumentOutOfRangeException(nameof(pivot));
        if (!float.IsFinite(previousScale) || previousScale <= 0) throw new ArgumentOutOfRangeException(nameof(previousScale));
        if (!float.IsFinite(nextScale) || nextScale <= 0) throw new ArgumentOutOfRangeException(nameof(nextScale));
        return pivot - (pivot - origin) * (nextScale / previousScale);
    }
}
