using System;

using Inno.Editor.Settings;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.GameView;

[EditorSettingPath(C_PATH)]
internal sealed class GameViewFramingSetting : EditorSetting
{
    internal const string C_PATH = "Editor/Appearance/Viewports/Game Framing";
    private const int C_DEFAULT_ASPECT_WIDTH = 16;
    private const int C_DEFAULT_ASPECT_HEIGHT = 9;

    /// <summary>
    /// Gets a new value initialized to the canonical Game View framing preferences.
    /// </summary>
    public override EditorSettingObject defaultValue => CreateDefault();

    /// <summary>
    /// Gets the presentation section that groups Game View framing preferences.
    /// </summary>
    public override string section => "Game View";

    /// <summary>
    /// Gets the user-facing explanation of aspect-preserving Game View presentation.
    /// </summary>
    public override string description
        => "Preserve a preview aspect ratio by fitting the complete rendered image between black bars.";

    /// <summary>
    /// Draws the aspect-preservation toggle and positive integer ratio.
    /// </summary>
    /// <param name="setting">
    /// The isolated mutable Editor setting value currently being presented.
    /// </param>
    protected override void OnDraw(EditorSettingObject setting)
    {
        bool preserve = setting.GetAsBoolean("preserveAspectRatio", defaultValue: true);
        if (NativeImGui.Checkbox("Preserve Aspect Ratio", ref preserve))
            setting.SetAsBoolean("preserveAspectRatio", preserve);

        int width = Math.Max(1, setting.GetAsInt32("aspectWidth", C_DEFAULT_ASPECT_WIDTH));
        int height = Math.Max(1, setting.GetAsInt32("aspectHeight", C_DEFAULT_ASPECT_HEIGHT));
        NativeImGui.BeginDisabled(!preserve);
        try
        {
            if (NativeImGui.InputInt("Aspect Width", ref width))
                setting.SetAsInt32("aspectWidth", Math.Max(1, width));
            if (NativeImGui.InputInt("Aspect Height", ref height))
                setting.SetAsInt32("aspectHeight", Math.Max(1, height));
        }
        finally
        {
            NativeImGui.EndDisabled();
        }
    }

    internal static GameViewFraming Read(EditorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EditorSettingObject value = settings.Get(C_PATH);
        return new GameViewFraming(
            value.GetAsBoolean("preserveAspectRatio", defaultValue: true),
            Math.Max(1, value.GetAsInt32("aspectWidth", C_DEFAULT_ASPECT_WIDTH)),
            Math.Max(1, value.GetAsInt32("aspectHeight", C_DEFAULT_ASPECT_HEIGHT)));
    }

    private static EditorSettingObject CreateDefault()
    {
        var result = new EditorSettingObject();
        result.SetAsBoolean("preserveAspectRatio", true);
        result.SetAsInt32("aspectWidth", C_DEFAULT_ASPECT_WIDTH);
        result.SetAsInt32("aspectHeight", C_DEFAULT_ASPECT_HEIGHT);
        return result;
    }
}

internal readonly record struct GameViewFraming(
    bool preserveAspectRatio,
    int aspectWidth,
    int aspectHeight);
