# Inno.Editor.Panel.Global

[Editor 索引](README.md) · [Settings API](Inno.Editor.Settings.md) · [Interactions](Inno.Editor.Interactions.md) · [Wiki 首页](../README.md)

`Inno.Editor.Panel.Global` 是 Editor Application 的全局 feature composition 项目。它不提供一个可停靠 Panel，也没有公开 API；它只放置需要由宿主统一发现、但不应属于 Settings 或 Interactions 基础程序集的内建定义。

## 职责与边界

```text
Inno.Editor.Panel.Global/
├─ Actions/
│  ├─ HistoryActions.cs
│  ├─ SelectionActions.cs
│  └─ TogglePanelAction.cs
├─ Settings/
│  ├─ GlobalSettingsPages.cs
│  └─ IconSettings.cs
└─ Zoom/
   ├─ EditorZoomModule.cs
   ├─ ZoomActions.cs
   └─ ActualSizeSetting.cs
```

Zoom 的持久设置、session 倍率 Module 与三个 Action 作为一个 feature 放在 `Zoom`；其他全局历史、选择和 Panel Action 继续位于 `Actions`。目录表达业务归属，不再按 Runtime/Action/Setting 类型机械拆散同一功能。

- `Inno.Editor.Settings` 只保留机制；本项目拥有 Global、Appearance、Icons 页面和实际字段。
- `Inno.Editor.Interactions` 只保留路由与 History 机制；本项目拥有宿主默认的 Undo、Redo、Selection 和 Toggle Panel action。
- 所有类型都是 internal，并由 TypeCache 根据 Attribute 发现。Application 通过项目引用确保程序集被加载。
- Attribute 与运行时调用都集中使用项目根目录 `GlobalInteractionIds` 中的 `const string`，不散落字符串 literal。Settings path 仍按当前契约使用原始完整字符串。

## 全局 Settings

当前定义包括：

| 路径 | 对象内容 | 消费者 |
| --- | --- | --- |
| `Global/Appearance/Accessibility/Actual Size` | Single 属性 `value` | `EditorZoomModule` |
| `Global/Appearance/Icons/Scene` | String 属性 `value` | Hierarchy、FileBrowser、Inspector |
| `Global/Appearance/Icons/GameObject` | String 属性 `value` | Hierarchy、Inspector |
| `Global/Appearance/Icons/Prefab` | String 属性 `value` | FileBrowser、Inspector |
| `Global/Appearance/Icons/Layers` | String 属性 `value` | Settings/Inspector presentation |
| `Global/Appearance/Icons/Folder` | String 属性 `value` | FileBrowser |
| `Global/Appearance/Icons/File` | String 属性 `value` | FileBrowser fallback |

每个 icon 是独立的 `EditorSetting` field，并在自己的 `OnDraw(EditorSettingObject)` 中绘制 ImGui glyph selector。Selector 的关闭预览和弹出选项使用同一个最大 icon slot；每个 glyph 再按 baked font 的真实可见边界居中，因此 File、Folder 与较宽的 Cubes 等轮廓中心保持在同一竖线上，label 也从同一位置开始。消费者直接调用 `EditorSettings.Get("...")`，再读取 `value`；Settings 内核不会解析 icon，也不导出路径常量。

Actual Size field 同样直接绘制选择器，并通过 Settings Modal 的 Apply 进入统一 Undo/Redo。Zoom In/Out 只改变以 actual size 为基准的 session 倍率，Actual Size action 只清除临时倍率；这三个快捷键不会改 `EditorSettings.json`，也不会制造 Settings History。

## 内建 Actions

| ID | Area/Menu | 行为 |
| --- | --- | --- |
| `editor/undo` | `editor/main-menu` → `Edit/Undo` | 查询并撤销共享 `EditorHistory` 顶部记录。 |
| `editor/redo` | `editor/main-menu` → `Edit/Redo` | 查询并重做共享 `EditorHistory` 顶部记录。 |
| `editor/select` | 无固定 area | 把 action target 交给 `EditorInteractions.SetSelection`。 |
| `editor/clear-selection` | 无固定 area | 清空 session selection。 |
| `editor/toggle-panel` | `editor/main-menu` | 接收稳定 panel ID `string`，在当前 snapshot 解析 descriptor 后切换受控 `isOpen`。 |
| `editor.ui.zoom-in` | `editor/main-menu` → `View/Zoom In` | 增加一个 actual-size 倍率步长。 |
| `editor.ui.zoom-out` | `editor/main-menu` → `View/Zoom Out` | 减少一个 actual-size 倍率步长。 |
| `editor.ui.zoom-reset` | `editor/main-menu` → `View/Actual Size` | 恢复配置的 actual size。 |

这些 action 是宿主默认行为，不是 Interactions 稳定公开契约。Feature action 仍放在各自项目中；Attribute、执行、排队和呈现共用同一 `const string` action ID。

## 依赖与初始化

项目只引用 Input、native/platform ImGui、Editor ImGui、Interactions 和 Settings。引用均为实现依赖，因为项目没有 public/protected 签名。它不引用 Application、具体 Panel、Asset、Scene 或 Inspector，因而不会形成 feature 间反向依赖。

Application 在创建 TypeCache 之前加载本程序集。之后 Settings Catalog 与 Action Catalog 在候选 generation 中发现这些 internal 类型并与脚本定义一起原子激活。

## 扩展规则

- 新的全局宿主行为可以放在本项目；具体 Scene/Asset/Inspector 行为仍留在所属 feature。
- 新字段继承非泛型 `EditorSetting`，默认值与当前值都使用 `EditorSettingObject`。
- 不向本项目增加公开 facade、图标 resolver、built-in draw context 或路径常量层。
- Settings 值修改统一调用 `EditorSettings.Apply`，不创建 feature 专属 Undo/Redo action。

[上一页：Inno.Editor.Panel.FileBrowser](Inno.Editor.Panel.FileBrowser.md) · [下一页：Inno.Editor.Panel.Hierarchy](Inno.Editor.Panel.Hierarchy.md)
