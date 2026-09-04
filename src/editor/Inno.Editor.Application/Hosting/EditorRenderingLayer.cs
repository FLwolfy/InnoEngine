using Inno.Rendering.Runtime;

namespace Inno.Editor.Application;

internal sealed class EditorRenderingLayer : EditorHostLayer
{
    private readonly RenderRuntimeLayer m_runtime;

    internal EditorRenderingLayer(RenderRuntimeLayer runtime)
    {
        m_runtime = runtime;
    }

    /// <summary>
    /// Activates the rendering runtime after this adapter joins the editor layer stack.
    /// </summary>
    internal override void Attach()
        => m_runtime.OnAttach();

    /// <summary>
    /// Opens the rendering frame before editor render requests are submitted.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed editor frame time in seconds.
    /// </param>
    internal override void BeginRender(float deltaTime)
        => m_runtime.OnBeforeRender(deltaTime);

    /// <summary>
    /// Collects request-provider rendering work for the current editor frame.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed editor frame time in seconds.
    /// </param>
    internal override void Render(float deltaTime)
        => m_runtime.OnRender(deltaTime);

    /// <summary>
    /// Compiles, executes, and closes the current editor rendering frame.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed editor frame time in seconds.
    /// </param>
    internal override void EndRender(float deltaTime)
        => m_runtime.OnAfterRender(deltaTime);

    /// <summary>
    /// Releases rendering generations after this adapter leaves the editor layer stack.
    /// </summary>
    internal override void Detach()
        => m_runtime.OnDetach();
}
