using System;
using InnoEditor.Core;
using InnoEditor.Interactions;
using InnoEngine.Logging;

/// <summary>Exercises the public File Browser creation action in an explicitly isolated acceptance project.</summary>
[EditorModule("tests.shader-editor.acceptance", order: 900)]
public sealed class ShaderEditorAcceptance(EditorInteractions interactions) : EditorModule
{
    private bool m_done;
    /// <inheritdoc />
    protected override void OnUpdate(EditorContext context)
    {
        if (m_done || Environment.GetEnvironmentVariable("INNO_SHADER_EDITOR_ACCEPTANCE") != "1") return;
        if (!interactions.For("panel/asset.file-browser", string.Empty).Execute("shader/create-asset"))
            throw new InvalidOperationException("Shader creation action is unavailable in the active Editor generation.");
        m_done = true;
        Log.Info("Shader Editor acceptance: File Browser creation and selection-follow action completed.");
    }
}
