using Inno.Core.Logging;

namespace Inno.Editor.Core;

/// <summary>
/// Shared runtime context used by all editor panels.
/// </summary>
public sealed class EditorContext
{
    /// <summary>
    /// Gets the shared selection state.
    /// </summary>
    public EditorSelectionState selection { get; } = new();

    /// <summary>
    /// Gets the in-memory log buffer backing the log panel.
    /// </summary>
    public EditorLogBuffer logs { get; } = new();

    /// <summary>
    /// Gets or sets the latest frame delta in seconds.
    /// </summary>
    public float frameDeltaTime { get; set; }

    /// <summary>
    /// Gets or sets the latest absolute runtime in seconds.
    /// </summary>
    public float totalTime { get; set; }

    /// <summary>
    /// Registers editor-wide services into global systems.
    /// </summary>
    public void Attach()
    {
        LogManager.RegisterSink(logs);
    }

    /// <summary>
    /// Unregisters editor-wide services from global systems.
    /// </summary>
    public void Detach()
    {
        LogManager.UnregisterSink(logs);
    }
}
