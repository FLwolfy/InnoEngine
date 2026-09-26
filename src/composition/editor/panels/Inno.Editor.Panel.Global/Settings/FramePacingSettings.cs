using System;
using Inno.Editor.Core;
using Inno.Editor.Settings;
using Inno.Platform;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Global;

[EditorSettingPath("Editor/Rendering")]
internal sealed class EditorRenderingSettingsPage : EditorSetting
{
    /// <summary>
    /// Gets the user-facing explanation of this feature or setting.
    /// </summary>
    public override string description => "Control editor presentation independently of game simulation timing.";
}

[EditorSettingPath("Editor/Rendering/Vertical Sync", order: 10)]
internal sealed class VerticalSyncSetting : EditorSetting
{
    /// <summary>
    /// Gets a new value initialized to this setting's canonical default state.
    /// </summary>
    public override EditorSettingObject defaultValue
    {
        get
        {
            var result = new EditorSettingObject();
            result.SetAsBoolean("enabled", false);
            return result;
        }
    }
    /// <summary>
    /// Gets the presentation section that groups this setting.
    /// </summary>
    public override string section => "Frame Rate";
    /// <summary>
    /// Gets the user-facing explanation of this feature or setting.
    /// </summary>
    public override string description => "Synchronize presentation to the display refresh rate. Disable to allow higher frame rates.";
    /// <summary>
    /// Draws this feature using the current editor presentation context.
    /// </summary>
    /// <param name="setting">
    /// The mutable editor setting value currently being presented.
    /// </param>
    protected override void OnDraw(EditorSettingObject setting)
    {
        bool enabled = setting.GetAsBoolean("enabled", false);
        if (NativeImGui.Checkbox("##vsync", ref enabled))
            setting.SetAsBoolean("enabled", enabled);
    }
}

[EditorSettingPath("Editor/Rendering/Maximum Frame Rate", order: 20)]
internal sealed class MaximumFrameRateSetting : EditorSetting
{
    /// <summary>
    /// Gets a new value initialized to this setting's canonical default state.
    /// </summary>
    public override EditorSettingObject defaultValue
    {
        get
        {
            var result = new EditorSettingObject();
            result.SetAsInt32("value", 0);
            return result;
        }
    }
    /// <summary>
    /// Gets the presentation section that groups this setting.
    /// </summary>
    public override string section => "Frame Rate";
    /// <summary>
    /// Gets the user-facing explanation of this feature or setting.
    /// </summary>
    public override string description => "Software frame-rate limit. Zero means Unlimited. Fixed-step simulation is unaffected.";
    /// <summary>
    /// Draws this feature using the current editor presentation context.
    /// </summary>
    /// <param name="setting">
    /// The mutable editor setting value currently being presented.
    /// </param>
    protected override void OnDraw(EditorSettingObject setting)
    {
        int rate = setting.GetAsInt32("value", 0);
        if (NativeImGui.InputInt("##maximum_fps", ref rate))
            setting.SetAsInt32("value", Math.Clamp(rate, 0, 1000));
        if (rate == 0)
            NativeImGui.TextDisabled("Unlimited");
    }
}

[EditorModule("editor-frame-pacing", order: 20)]
internal sealed class EditorFramePacingModule(EditorSettings settings, FramePacingOptions pacing) : EditorModule
{
    /// <summary>
    /// Initializes this feature when its owning runtime becomes active.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnStart(EditorContext context)
    {
        Apply(settings);
        settings.changed += Apply;
    }
    /// <summary>
    /// Stops this feature before its owning runtime releases the active generation.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnStop(EditorContext context) => settings.changed -= Apply;

    private void Apply(EditorSettings current)
    {
        pacing.verticalSync = current.Get("Editor/Rendering/Vertical Sync").GetAsBoolean("enabled", false);
        pacing.maximumFrameRate = Math.Clamp(
            current.Get("Editor/Rendering/Maximum Frame Rate").GetAsInt32("value", 0), 0, 1000);
    }
}
