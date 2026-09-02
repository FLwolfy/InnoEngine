using System.Numerics;

using Inno.Editor.Settings;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.GameView;

[EditorSettingPath(C_PATH)]
internal sealed class GameViewBackgroundSetting : EditorSetting
{
    internal const string C_PATH = "Editor/Appearance/Viewports/Game Background";
    private static readonly float[] S_DEFAULT = [0.035f, 0.04f, 0.05f, 1f];

    /// <summary>
    /// Gets a new value initialized to this setting's canonical default state.
    /// </summary>
    public override EditorSettingObject defaultValue => CreateDefault();

    /// <summary>
    /// Gets the presentation section that groups this setting.
    /// </summary>
    public override string section => "Viewport Backgrounds";

    /// <summary>
    /// Gets the user-facing explanation of this feature or setting.
    /// </summary>
    public override string description => "Choose the default clear color supplied to the Game View provider.";

    /// <summary>
    /// Draws this feature using the current editor presentation context.
    /// </summary>
    /// <param name="setting">
    /// The mutable editor setting value currently being presented.
    /// </param>
    protected override void OnDraw(EditorSettingObject setting)
    {
        Vector4 value = ReadVector(setting);
        NativeImGui.SetNextItemWidth(-1f);
        if (NativeImGui.ColorEdit4("##game_view_background", ref value))
            setting.SetAsSingleArray("value", [value.X, value.Y, value.Z, value.W]);
    }

    internal static Vector4 Read(EditorSettings settings)
        => ReadVector(settings.Get(C_PATH));

    private static EditorSettingObject CreateDefault()
    {
        var result = new EditorSettingObject();
        result.SetAsSingleArray("value", S_DEFAULT);
        return result;
    }

    private static Vector4 ReadVector(EditorSettingObject setting)
    {
        float[] values = setting.GetAsSingleArray("value", S_DEFAULT);
        return values.Length == 4
            ? new Vector4(values[0], values[1], values[2], values[3])
            : new Vector4(S_DEFAULT[0], S_DEFAULT[1], S_DEFAULT[2], S_DEFAULT[3]);
    }
}
