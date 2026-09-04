using System;

namespace Inno.Editor.Core;

/// <summary>
/// Defines the presentation-independent lifecycle of an editor runtime.
/// </summary>
public abstract class EditorRuntime : IDisposable
{
    /// <summary>
    /// Creates a runtime for the supplied passive context.
    /// </summary>
    /// <param name="context">
    /// The shared editor context.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    protected EditorRuntime(EditorContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Gets the shared passive editor context.
    /// </summary>
    public EditorContext context { get; }

    /// <summary>
    /// Starts the runtime and activates its initial extension generation.
    /// </summary>
    public abstract void Start();

    /// <summary>
    /// Updates the runtime for one editor frame.
    /// </summary>
    /// <param name="frame">
    /// The immutable frame state.
    /// </param>
    public abstract void Update(EditorFrame frame);

    /// <summary>
    /// Advances frame-scoped data and publishes the latest immutable frame.
    /// </summary>
    /// <param name="frame">
    /// The frame to publish.
    /// </param>
    protected void SetFrame(EditorFrame frame)
    {
        context.statistics.AdvanceFrame();
        context.frame = frame;
    }

    /// <summary>
    /// Stops the runtime and releases active extensions.
    /// </summary>
    public abstract void Dispose();
}
