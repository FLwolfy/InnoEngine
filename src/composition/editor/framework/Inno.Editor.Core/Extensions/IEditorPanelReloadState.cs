using System;

namespace Inno.Editor.Core;

/// <summary>
/// Optionally migrates presentation-neutral panel state across assembly reloads.
/// </summary>
public interface IEditorPanelReloadState
{
    /// <summary>
    /// Captures presentation-neutral state without retaining runtime or plugin object references.
    /// </summary>
    /// <returns>
    /// An immutable byte payload that can be consumed by the replacement panel generation.
    /// </returns>
    ReadOnlyMemory<byte> CaptureReloadState();

    /// <summary>
    /// Restores presentation-neutral state captured from the previous panel generation.
    /// </summary>
    /// <param name="state">
    /// The immutable payload returned by the retiring panel generation.
    /// </param>
    void RestoreReloadState(ReadOnlyMemory<byte> state);
}
