using System;

using Inno.Core.Settings;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Editor.Settings;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Settings;

[ProjectSettingPath("Project/Identity/Project ID")]
internal sealed class ProjectIdentitySettingsEditor : ProjectSettingEditor<ProjectIdentitySettings>
{
    private string m_error = string.Empty;

    /// <summary>
    /// Gets or sets the setting id exposed by this implementation.
    /// </summary>
    public override ProjectSettingId settingId => ProjectIdentitySettings.settingId;

    /// <summary>
    /// Gets the section exposed by this implementation.
    /// </summary>
    public override string section => "Identity";

    /// <summary>
    /// Gets the description exposed by this implementation.
    /// </summary>
    public override string description
        => "Defines the project namespace. Project-owned IDs are resolved as projectId.name at runtime.";

    /// <summary>
    /// Draws this feature using the current editor presentation context.
    /// </summary>
    /// <param name="setting">
    /// The setting supplied to this operation.
    /// </param>
    protected override void OnDraw(ProjectIdentitySettings setting)
    {
        string value = setting.projectId;
        if (NativeImGui.InputText("Project ID", ref value, 129))
        {
            try
            {
                setting.projectId = value;
                m_error = string.Empty;
            }
            catch (ArgumentException exception)
            {
                m_error = exception.Message;
            }
        }
        if (!string.IsNullOrEmpty(m_error))
            ImGuiWidget.ColoredText(EditorPalette.error, m_error);
    }
}
