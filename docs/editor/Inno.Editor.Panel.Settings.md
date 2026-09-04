# Inno.Editor.Panel.Settings

[Editor 索引](README.md) · [Settings API](Inno.Editor.Settings.md) · [Project Settings](../core/Inno.Core.Settings.md) · [Wiki 首页](../README.md)

`Inno.Editor.Panel.Settings` 是统一 Settings frontend。它把三种不同生命周期的设置放在同一个可搜索窗口中，但明确显示为三个根：

```text
Settings
├─ Editor/...   → Settings.Editor.inno
├─ Project/...  → Settings.Project.inno
└─ Build/...    → Settings.Build.inno
```

## 窗口行为

- 主菜单 `Edit/Settings...` 打开可移动、可缩放但不可 Dock/Collapse 的 Modal。
- 搜索框左侧提供 Back/Forward 导航按钮。两个按钮使用与搜索输入框相同的当前 `GetFrameHeight()`，方形 hit area、上下边界与垂直中心完全一致；Tree 点击、页面内链接与开始搜索都会形成页面历史，连续输入搜索词只替换当前搜索结果，不会为每个字符制造历史项。
- 左侧 Tree 合并 `EditorSetting`、`ProjectSettingEditor` 与内置 `BuildSettings` 字段；搜索匹配 page、path、label、section 与 description。
- 右侧每个完整字段使用自动内容行高；label/content/reset 保持对齐，连续行之间没有空隙。Field Table 严格使用 page 的真实 content width，不通过负 cursor 或扩大 table width 穿透 padding，因此不会污染 `CursorMaxPos` 或产生虚假水平滚动范围。左右 gutter 背景作为不参与 layout 的 draw-list geometry 延伸至内容窗口边缘，文字与控件继续使用正常 window/cell inset。两种背景使用轻微明度差和固定 `0.005` alpha，只辅助辨认连续字段而不形成明显色块。
- 合成页面不需要中央 page 注册；frontend 根据 slash-delimited path 自动补齐祖先。
- Catalog generation 改变时丢弃旧 staged generation，按新 definitions 原子重建窗口 session。

## 单一 Apply 操作

底部根据 staged state 使用两种互斥状态：没有变化时只显示右对齐的 `OK`；存在变化时显示右对齐的 `Cancel` 与 `Apply`，其中 `Cancel` 在左、`Apply` 在右：

- `Apply` 一次提交所有 dirty scope；Editor、Project、Build 分别原子写入 `Settings.Editor.inno`、`Settings.Project.inno`、`Settings.Build.inno`，并形成各自的 History entry。
- `Cancel` 同时丢弃三个域尚未 Apply 的隔离 staged 值并关闭窗口。
- `OK` 仅在 session clean 时关闭窗口，不执行无意义的持久化。
- `Apply` 仅在 staged effective value 相对窗口打开或上次 Apply 的基线真正变化时启用。把一次未提交修改 Reset 回原始默认值不会留下虚假的 reset intent。
- 三个持久化域仍分别保证各自的原子写入与 History；单一按钮不把三个文件伪装成一个跨文件事务。

Reset Editor 恢复字段定义的 `defaultValue`。Reset Project 删除项目 override，并恢复 Host 默认值与依赖有序 Plugin 默认贡献的合成结果。Reset Build 恢复由项目名、host target 和首个已导入 Scene 建立的 project-derived 默认值。页面顶部 Reset 会递归作用于当前页面所有后代字段，但提交仍遵循各自数据域。

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

同一个 `ProjectSettingId` 可以注册多个 `ProjectSettingEditor<TSetting>` presentation，前提是它们使用完全相同的 `TSetting`。不同 presentation 可以使用同一 `pagePath` 和不同 `section`，由 frontend 绘制为同级、全宽的分节横线；它们共享一个 staged setting、Reset、dirty 判断与 Apply，不复制运行时配置对象。Renderer 与 Sorting Layers 这类同属一个设置协议、但视觉上应分节的内容应采用这种组合方式，不应在 `OnDraw` 内手写嵌套 separator。

Settings frontend 不内建 Boolean、Layer、Tag、PBR 或其他业务字段类型。Game Layers/Tags 的 Drawer 当前由 Inspector feature 提供；Plugin 可以以同一协议增加自己的页面而不修改 Panel。

## 内部生命周期

`SettingsEditSession` 按 scope 分别保存 staged objects、modified IDs、初始 effective-default 状态与必要的 reset intent。Editor 值用定义 baseline 比较；Project 值使用 Inno Serialization property bytes 比较；Build 值是强类型隔离副本。单一 Apply 只处理 dirty scope，并在成功后刷新对应基线。

所有 ImGui Begin/End、Push/Pop 与 disabled scope 均在 `try/finally` 中配对。某个 extension 绘制失败由 Editor generation quarantine 隔离，不允许污染后续 ImGui stack。

该项目没有公开类型；业务扩展只引用 [Inno.Editor.Settings](Inno.Editor.Settings.md)，不引用本 Panel。

[上一页：Inno.Editor.Settings](Inno.Editor.Settings.md) · [下一页：Inno.Editor.Panel.Stats](Inno.Editor.Panel.Stats.md)
