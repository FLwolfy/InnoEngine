namespace Inno.Core.Framework;

public static class Time
{
    /// <summary>
    /// Time elapsed since game start in seconds.
    /// </summary>
    public static float time { get; private set; }

    /// <summary>
    /// Time elapsed since last frame in seconds.
    /// </summary>
    public static float deltaTime { get; private set; }
    
    /// <summary>
    /// The time between render updates.
    /// </summary>
    public static float renderDeltaTime { get; private set; }

    /// <summary>
    /// Update the time info. Called each frame from the game loop.
    /// </summary>
    /// <param name="totalTime">Total time since start, in seconds.</param>
    /// <param name="delta">Delta time for this frame, in seconds.</param>
    internal static void Update(float totalTime, float delta)
    {
        time = totalTime;
        deltaTime = delta;
    }

    /// <summary>
    /// Update the render delta time. This is called by the render pipeline after each frame is rendered.
    /// </summary>
    /// <param name="delta">Delta time between render frames.</param>
    internal static void RenderUpdate(float delta)
    {
        renderDeltaTime = deltaTime;
    }
}