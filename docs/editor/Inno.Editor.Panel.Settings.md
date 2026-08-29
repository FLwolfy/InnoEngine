# Inno.Editor.Panel.Settings

[Editor 索引](README.md) · [Settings API](Inno.Editor.Settings.md) · [Project Settings](../core/Inno.Core.Settings.md) · [Wiki 首页](../README.md)

`Inno.Editor.Panel.Settings` 是统一 Settings frontend。它把 Editor-only JSON 设置与 runtime Project 设置放在同一个可搜索窗口中，但明确显示为两个根：

```text
Settings
├─ Editor/...   → EditorSettings.json
└─ Project/...  → ProjectSettings.inno
```

## 窗口行为

- 主菜单 `Edit/Settings...` 打开可移动、可缩放但不可 Dock/Collapse 的 Modal。
- 左侧 Tree 合并 `EditorSetting` 与 `ProjectSettingEditor` 的 placement；搜索匹配 page、path、label、section 与 description。
- 右侧每个完整字段使用自动内容行高；label/content/reset 保持对齐，连续行之间没有空隙。Field Table 横向越过 page content padding，让背景严格贴合内容窗口左右边缘；文字与控件单独保留正常 window/cell inset。两种背景使用更明亮的灰色 RGB 与固定 `0.1` alpha。字段自身仍拥有具体 ImGui 控件。
- 合成页面不需要中央 page 注册；frontend 根据 slash-delimited path 自动补齐祖先。
- Catalog generation 改变时丢弃旧 staged generation，按新 definitions 原子重建窗口 session。

## 单一 Apply 操作

底部只显示右对齐的 `Cancel` 与 `Apply`，其中 `Cancel` 在左、`Apply` 在右：

- `Apply` 一次提交所有 dirty scope；Editor 部分原子写入 `EditorSettings.json` 并形成 `Apply Settings` History entry，Project 部分原子写入 `ProjectSettings.inno` 并形成 `Apply Project Settings` History entry。
- `Cancel` 同时丢弃两个域尚未 Apply 的隔离 staged 值并关闭窗口。
- `Apply` 仅在 staged effective value 相对窗口打开或上次 Apply 的基线真正变化时启用。把一次未提交修改 Reset 回原始默认值不会留下虚假的 reset intent。
- 两个持久化域仍分别保证各自的原子写入与 History；单一按钮不把两个文件伪装成一个跨文件事务。

Reset Editor 恢复字段定义的 `defaultValue`。Reset Project 删除项目 override，并恢复 Host 默认值与依赖有序 Plugin 默认贡献的合成结果。页面顶部 Reset 会递归作用于当前页面所有后代字段，但提交仍遵循各自数据域。

## 扩展方式

Editor-only 字段：

```csharp
[EditorSettingPath("Editor/MyTool/Snap")]
public sealed class SnapSetting : EditorSetting
{
    // Define defaultValue and OnDraw(EditorSettingObject).
}
```

Runtime/Plugin 字段：

```csharp
[ProjectSettingPath("Project/MyPlugin/Rendering")]
public sealed class RenderingSettingsEditor
    : ProjectSettingEditor<MyRenderingSettings>
{
    public override ProjectSettingId settingId => MyRenderingSettings.settingId;

    protected override void OnDraw(MyRenderingSettings setting)
    {
        // Draw through InnoEditor.ImGui and mutate only this staged snapshot.
    }
}
```

Settings frontend 不内建 Boolean、Layer、Tag、PBR 或其他业务字段类型。Game Layers/Tags 的 Drawer 当前由 Inspector feature 提供；Plugin 可以以同一协议增加自己的页面而不修改 Panel。

## 内部生命周期

`SettingsEditSession` 按 scope 分别保存 staged objects、modified IDs、初始 effective-default 状态与必要的 reset intent。Editor 值用定义 baseline 比较；Project 值使用 Inno Serialization property bytes 比较。单一 Apply 只处理 dirty scope，并在成功后刷新对应基线。

所有 ImGui Begin/End、Push/Pop 与 disabled scope 均在 `try/finally` 中配对。某个 extension 绘制失败由 Editor generation quarantine 隔离，不允许污染后续 ImGui stack。

该项目没有公开类型；业务扩展只引用 [Inno.Editor.Settings](Inno.Editor.Settings.md)，不引用本 Panel。

[上一页：Inno.Editor.Settings](Inno.Editor.Settings.md) · [下一页：Inno.Editor.Panel.Stats](Inno.Editor.Panel.Stats.md)
