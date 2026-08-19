using System;

namespace Inno.Editor.Core.Panels;

/// <summary>Optionally migrates presentation-neutral panel state across assembly reloads.</summary>
public interface IEditorPanelReloadState
{
    /// <summary>Captures state without retaining runtime or plugin object references.</summary>
    ReadOnlyMemory<byte> CaptureReloadState();

    /// <summary>Restores state captured from the previous panel generation.</summary>
    void RestoreReloadState(ReadOnlyMemory<byte> state);
}
