using System;

using Inno.Editor.Core;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;

namespace Inno.Editor.ImGui;

/// <summary>
/// Owns the project-restored global editor UI zoom state.
/// </summary>
[EditorModule(order: 10)]
internal sealed class EditorZoomModule : EditorModule, IEditorWorkspaceState
{
    /// <inheritdoc />
    public string workspaceStateId => "editor-ui-zoom";

    internal float zoom => EditorWidget.style.zoom;

    internal bool ZoomIn() => EditorWidget.style.ZoomIn();

    internal bool ZoomOut() => EditorWidget.style.ZoomOut();

    internal bool Reset() => EditorWidget.style.ResetZoom();

    /// <inheritdoc />
    public void CaptureWorkspaceState(EditorWorkspaceStateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Set("zoom", zoom);
    }

    /// <inheritdoc />
    public void RestoreWorkspaceState(EditorWorkspaceStateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        float value = reader.Get("zoom", 1f);
        _ = EditorWidget.style.SetZoom(float.IsFinite(value) ? value : 1f);
    }
}
