# Inno.Editor.Settings

[Editor 索引](README.md) · [Settings 界面](Inno.Editor.Panel.Settings.md) · [Core Project Settings](../core/Inno.Core.Settings.md) · [Wiki 首页](../README.md)

`Inno.Editor.Settings` 为同一个 Settings 窗口提供两种明确的编辑协议，但不合并它们的持久化语义：

```text
Editor/...                                      Project/...
EditorSetting + EditorSettingObject             ProjectSettingEditor<TSetting>
             │                                               │
             ▼                                               ▼
EditorSettings.inno（Inno Serialization）        ProjectSettings.inno（Inno Serialization）
Editor-only、不会进入 Player                     Runtime/Plugin 可读取并进入构建
```

## Editor Settings

Editor Settings 用于主题、图标、缩放、面板行为等只影响 Editor 的偏好。路径必须为 `Editor` 或以 `Editor/` 开头；完整路径同时是注册身份、读取地址和 Inno Serialization property key。

```csharp
using InnoEditor.ImGui;
using InnoEditor.Settings;

[EditorSettingPath("Editor/Appearance/Grid/Visible")]
public sealed class GridVisibilitySetting : EditorSetting
{
    public override EditorSettingObject defaultValue
    {
        get
        {
            var value = new EditorSettingObject();
            value.SetAsBoolean("value", true);
            return value;
        }
    }

    public override string description => "Shows the authoring grid.";

    protected override void OnDraw(EditorSettingObject setting)
    {
        bool value = setting.GetAsBoolean("value", true);
        if (ImGui.Checkbox("##visible", ref value))
            setting.SetAsBoolean("value", value);
    }
}
```

没有 override `OnDraw` 的 `EditorSetting` 是页面定义；override 后是字段定义。`EditorSettingObject` 支持受控的 Boolean、整数、浮点、String 与数组 GetAs/SetAs 方法，不允许保存 `Type`、delegate、runtime object 或 GPU 资源。

唯一读取入口仍是原始路径：

```csharp
EditorSettingObject value = editorSettings.Get("Editor/Appearance/Grid/Visible");
bool visible = value.GetAsBoolean("value", true);
```

返回对象始终隔离；只有 `EditorSettings.Apply(values, resets)` 才通过 `SerializationRegistry` 原子更新 `EditorSettings.inno` 并写入统一 Editor History。`EditorSettings.changed` 用于刷新 Editor-only 消费者。

例如 Console 的保留策略只在 `Editor/Diagnostics/Console/Clear on Play` 注册和持久化，默认值为 `true`。Console backend 订阅 `EditorSettings.changed` 并在 Apply、Undo、Redo 后读取当前有效值；Console Panel 不再使用 `editor.ini` 或 toolbar 维护同名状态。

## Project Settings 的 Editor 表现

Project Settings 的运行时定义属于 [Inno.Core.Settings](../core/Inno.Core.Settings.md)。本项目只提供可选的 Editor Drawer 协议，使 Plugin 的强类型设置自动出现在同一个窗口的 `Project/...` 页面。

```csharp
using InnoEditor.ImGui;
using InnoEditor.Settings;
using InnoEngine.Settings;

[ProjectSettingPath("Project/MyPlugin/Rendering")]
public sealed class RenderingSettingEditor : ProjectSettingEditor<MyRenderingSettings>
{
    public override ProjectSettingId settingId => MyRenderingSettings.settingId;

    public override string description => "Configures the runtime rendering provider.";

    protected override void OnDraw(MyRenderingSettings setting)
    {
        bool enabled = setting.enabled;
        if (ImGui.Checkbox("##enabled", ref enabled))
            setting.enabled = enabled;
    }
}
```

`ProjectSettingEditor<TSetting>` 收到的是当前 generation 的隔离暂存副本。它只能修改该副本；统一 Apply 中的 Project scope 以“Host + Plugin 合成结果”为 baseline：有 Composer 的协议只写语义 delta，没有 Composer 的协议写完整 replacement。Reset Project 删除项目 contribution，随后重新使用 Host 默认值与 Plugin 默认贡献的合成结果。如果编辑结果等于 baseline，Apply 会自动移除已有 project record，不留下空 override。

同一个 `ProjectSettingId` 可以注册多个不同 placement 的 Editor 表现，只要它们的
`TSetting` 完全相同。所有表现共享同一份隔离暂存对象、dirty 状态、Reset、Apply 和
History 事务；这允许一个较大的运行时设置协议在 UI 中拆成多个完整 section，而不必
为了排版拆碎运行时数据模型。各 placement path 仍必须全局唯一。

## 公开 API

| API | 稳定语义 |
| --- | --- |
| `EditorSettingPathAttribute` | 把 Editor-only page/field 放入 `Editor/...`。 |
| `EditorSetting` | Editor-only page/field 定义，`OnDraw(EditorSettingObject)` 是绘制扩展点。 |
| `EditorSettingObject` | 使用当前 Inno Serialization 的隔离结构化值对象。 |
| `EditorSettings` | `EditorSettings.inno` 的读取、Apply、Reset、History 与变更通知。 |
| `ProjectSettingPathAttribute` | 把强类型 Project Setting Drawer 放入 `Project/...`。 |
| `ProjectSettingEditor` | frontend 使用的非泛型定义与 placement metadata。 |
| `ProjectSettingEditor<TSetting>` | Plugin/Host 实现的强类型 `OnDraw(TSetting)` 扩展点。 |
| `ProjectSettingsEditor` | Host-owned Project contribution staging、Apply 与 History 服务；普通业务脚本读取设置时应使用 `ProjectSettingsStore`。 |

## 生命周期与约束

- 两个域共享窗口、搜索、页面树、控件布局与一个 Apply 按钮，但仍拥有独立文档和 History entry，不伪装成跨文件原子事务。
- `Editor/...` 不参与 Plugin 默认贡献，也不进入 Player；`Project/...` 使用强类型 Setting 协议而不是 Editor property bag。
- Project setting 的长期身份是 `ProjectSettingId` 与 Stable Type ID，UI 路径只决定 Editor 中的位置。
- Catalog generation 先完整构建候选再原子切换；Drawer 不应订阅静态事件或长期保存传入的 setting 实例。
- 删除或移动路径时同步当前项目数据、调用方与 Wiki，不保留旧 key alias。

[上一页：Inno.Editor.Scene](Inno.Editor.Scene.md) · [下一页：Inno.Editor.Panel.Settings](Inno.Editor.Panel.Settings.md)
