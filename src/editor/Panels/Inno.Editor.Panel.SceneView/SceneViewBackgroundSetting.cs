using System.Numerics;

using Inno.Editor.Settings;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.SceneView;

[EditorSettingPath(C_PATH)]
internal sealed class SceneViewBackgroundSetting : EditorSetting
{
    internal const string C_PATH = "Editor/Appearance/Viewports/Scene Background";
    private static readonly float[] S_DEFAULT = [0.11f, 0.12f, 0.14f, 1f];

    /// <inheritdoc />
    public override EditorSettingObject defaultValue => CreateDefault();

    /// <inheritdoc />
    public override string section => "Viewport Backgrounds";

    /// <inheritdoc />
    public override string description => "Choose the default clear color supplied to the Scene View provider.";

    /// <inheritdoc />
    protected override void OnDraw(EditorSettingObject setting)
    {
        Vector4 value = ReadVector(setting);
        NativeImGui.SetNextItemWidth(-1f);
        if (NativeImGui.ColorEdit4("##scene_view_background", ref value))
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
