using System;
using Inno.Core.Math;

namespace Inno.Editor.Utility;

/// <summary>
/// Represents a 2D editor camera.
/// </summary>
/// <p>
/// Notes:
/// <list type="bullet">
///   <item><description>The editor uses ImGui with a left-handed coordinate system for rendering:
///       <list type="bullet">
///           <item><description>Origin is at the top-left corner of the screen.</description></item>
///           <item><description>X increases to the right.</description></item>
///           <item><description>Y increases downward.</description></item>
///       </list>
///   </description></item>
///   <item><description>This camera handles world-to-screen and screen-to-world conversions,
///       including panning and zooming, and accounts for the Y-axis inversion between world and screen coordinates.</description></item>
///   <item><description>World coordinates are typically Y-up, so conversions may invert Y to match ImGui.</description></item>
/// </list>
/// </p>
public class EditorCamera2D
{
    private float m_height;
    private float m_zoomRate = 1f;
    private float m_aspectRatio = 1.7777f;
    private Vector2 m_position = Vector2.ZERO;

    // TODO: THIS CAN BE MODIFIED LATER IN SETTINGS
    private const float C_NEAR = -1000f;
    private const float C_FAR = 1000f;
    
    private const float C_MIN_SIZE = 0.1f;
    private const float C_MAX_SIZE = 10f;
    private const float C_ZOOM_SPEED = 0.05f;
    
    // TODO: Possibly Use Cache for performance
    public Matrix viewMatrix => Matrix.CreateTranslation(-m_position.x, -m_position.y, 0);
    public Matrix projectionMatrix
    {
        get
        {
            float halfHeight = m_height * m_zoomRate * 0.5f;
            float halfWidth = halfHeight * m_aspectRatio;

            return Matrix.CreateOrthographic(halfWidth * 2, halfHeight * 2, C_NEAR, C_FAR);
        }
    }
    
    public float zoomRate => m_zoomRate;
    public float aspectRatio => m_aspectRatio;
    public Vector2 position => m_position;

    public void SetViewportSize(int width, int height)
    {
        if (height == 0) return;
        m_aspectRatio = (float)width / height;
        m_height = height;
    }

    public void Update(Vector2 panDelta, float zoomDelta, Vector2 localMousePosition)
    {
        Matrix screenToWorldMatrix = GetScreenToWorldMatrix();
        Vector2 worldPosBefore = Vector2.Transform(localMousePosition, screenToWorldMatrix);

        float zoomFactor = 1f + zoomDelta * C_ZOOM_SPEED;
        m_zoomRate = Math.Clamp(m_zoomRate / zoomFactor, C_MIN_SIZE, C_MAX_SIZE);

        screenToWorldMatrix = GetScreenToWorldMatrix();
        Vector2 worldPosAfter = Vector2.Transform(localMousePosition, screenToWorldMatrix);

        Vector2 panDeltaFlipY = new Vector2(panDelta.x, -panDelta.y); // Flip Y because ImGui Y increases downward
        m_position -= panDeltaFlipY * m_zoomRate;
        m_position += worldPosBefore - worldPosAfter;
    }
    
    public Matrix GetScreenToWorldMatrix()
    {
        return Matrix.Invert(GetWorldToScreenMatrix());
    }

    public Matrix GetWorldToScreenMatrix()
    {
        float halfWidth = m_height * aspectRatio / 2f;
        float halfHeight = m_height / 2f;

        Matrix screenToClip = Matrix.CreateScale(halfWidth, -halfHeight, 1f) // Flip Y because ImGui Y increases downward
                              * Matrix.CreateTranslation(halfWidth, halfHeight, 0f);


        return viewMatrix * projectionMatrix * screenToClip;
    }
}