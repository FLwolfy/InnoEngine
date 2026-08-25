# Editor API

[返回 Wiki 首页](../README.md) · [Core](../core/README.md) · [Assets](../assets/README.md)

Editor 采用“被动核心 → 后端无关交互 → ImGui 表现 → 独立 Panel feature → Application 组合根”的单向分层。Core 与 Interactions 不引用 Assets、Scene 或任何具体 Panel；业务行为由各 Panel 项目通过 Attribute 自发现。

## 项目索引

| 项目 | 职责 |
| --- | --- |
| [Inno.Editor.Core](Inno.Editor.Core.md) | `EditorContext`、frame/runtime、Module、Panel、Modal 与可选状态 hooks 的最小契约。 |
| [Inno.Editor.Interactions](Inno.Editor.Interactions.md) | Action、area、menu、shortcut、selection、drag/drop、Undo/Redo、Module/Panel 状态存储与扩展代际。 |
| [Inno.Editor.Scene](Inno.Editor.Scene.md) | Scene document workspace、细粒度 Scene 编辑门面与 reload-safe History 协议。 |
| [Inno.Editor.Settings](Inno.Editor.Settings.md) | 路径即身份的项目 Settings、`EditorSettingObject`、统一 Apply History 与根目录存储。 |
| [Inno.Editor.ImGui](Inno.Editor.ImGui.md) | ImGui runtime、renderer、统一 Widget、Palette 与 Style metrics。 |
| [Inno.Editor.Inspection](Inno.Editor.Inspection.md) | InspectionDrawer、PropertyDrawer、Registry 与 serialized property renderer。 |
| [Inno.Editor.Scripting](Inno.Editor.Scripting.md) | Asset-backed Roslyn 编译、facade、IDE 工程与热重载。 |
| [Inno.Editor.Panel.FileBrowser](Inno.Editor.Panel.FileBrowser.md) | AssetEditor、文件浏览、Asset 操作与 Asset-side drag/drop。 |
| [Inno.Editor.Panel.Global](Inno.Editor.Panel.Global.md) | internal 全局 Action、Global/Appearance 页面、Icon 与 Zoom setting definitions。 |
| [Inno.Editor.Panel.Hierarchy](Inno.Editor.Panel.Hierarchy.md) | Scene workspace、Hierarchy、Scene/GameObject 操作与排序。 |
| [Inno.Editor.Panel.Inspector](Inno.Editor.Panel.Inspector.md) | Inspector Panel、Scene Drawer 与 Component/System 操作。 |
| [Inno.Editor.Panel.Logging](Inno.Editor.Panel.Logging.md) | Editor 日志/诊断缓冲与 Console Panel。 |
| [Inno.Editor.Panel.Settings](Inno.Editor.Panel.Settings.md) | 可缩放阻塞 Modal、可搜索 Page Tree、overview 与 Section field frontend。 |
| [Inno.Editor.Panel.Stats](Inno.Editor.Panel.Stats.md) | 平滑后的帧统计与 Stats Panel。 |
| [Inno.Editor.Application](Inno.Editor.Application.md) | Platform、Shell、ImGui 和全部 feature 的组合根。 |

## 依赖方向

```mermaid
flowchart TD
    Core["Inno.Editor.Core"] --> Interactions["Inno.Editor.Interactions"]
    Core --> ImGui["Inno.Editor.ImGui"]
    Interactions --> ImGui
    Core --> Inspection["Inno.Editor.Inspection"]
    Interactions --> Inspection
    ImGui --> Inspection
    Core --> Scene["Inno.Editor.Scene"]
    Interactions --> Scene
    Core --> Settings["Inno.Editor.Settings"]
    Settings --> ImGui
    Settings --> Panels
    Scene --> Inspection
    Inspection --> Panels
    Scene --> Panels["Inno.Editor.Panel.*"]
    Core --> Panels
    Interactions --> Panels
    ImGui --> Panels
    Core --> Scripting["Inno.Editor.Scripting"]
    ImGui --> Scripting
    Panels --> Application["Inno.Editor.Application"]
    Scripting --> Application
    ImGui --> Application
```

箭头表示基础能力流向使用者。各 Panel/feature project 不通过具体 Panel 类型互相耦合；跨功能操作只传递共享领域类型或注入基础服务，例如 `AssetFileEntry`、`AssetInfo`、`GameScene`、`GameObject` 和 `EditorSettings`。

## 源码与依赖约定

- 每个 Editor 项目的源码统一使用项目名作为物理 namespace；功能目录不产生子 namespace。例如 `Inno.Editor.Inspection/PropertyDrawing/Drawers` 中的类型仍使用 `Inno.Editor.Inspection`。
- 唯一例外是 `Inno.Editor.ImGui/Widgets`，其 namespace 固定为 `Inno.Editor.ImGui.ImGuiWidget`，且目录中只允许 `ImGuiWidget.*.cs`。
- 目录按功能命名，不使用 `Internal` 目录表达访问级别。
- 每个 Editor `.csproj` 的第一个 ProjectReference `ItemGroup` 保存实现依赖并设置 `PrivateAssets="compile"`；第二个分组只保留真正出现在 public/protected API 中的传递依赖。

## 扩展入口

- 新 Panel：继承 `EditorPanel` 并添加 `[EditorPanel]`。
- 新操作：继承 `EditorAction` 或 `EditorAction<TTarget>` 并添加 `[EditorAction]`。
- 右键或主菜单：在 Action 上添加任意层级的 `[EditorMenu(area, "A/B/C")]`。
- 动态菜单：继承 `EditorMenuSource` 并添加 `[EditorMenuSource(area)]`。
- 拖放：继承 `EditorDrop<TSource,TTarget>` 并添加 `[EditorDrop(area)]`。
- 选择、焦点和打开等交互：通过 `interactions.For(area, target)` 获取轻量 `EditorInteraction`。
- 可撤销操作：领域 Module 先完成修改，再用中立 `EditorHistoryChange` 与 `[EditorHistoryHandler]` 记录；连续值可设置稳定 `mergeKey`，复合修改使用 transaction。
- 项目语义状态：Module/Panel 使用 Attribute 的唯一 ID，并 override protected `Capture(EditorState)` / `Restore(EditorState)`；扩展只使用 `state.Get` / `state.Set`，未 override Capture 的类型完全不进入状态 IO。
- 用户可配置项：声明 `[EditorSettingPath("A/B/Field")]` 并继承非泛型 `EditorSetting`；page 保留默认 `OnDraw`，field 用 `EditorSettingObject` 默认值并 override `OnDraw(EditorSettingObject)`。业务读取只调用 `EditorSettings.Get(path)`。
- 新检查器：业务项目引用 `Inno.Editor.Inspection`，继承 `InspectionDrawer<TTarget>` 或实现 `IPropertyDrawer` 并添加对应 Attribute；无需引用 Inspector Panel。

具体例子见 [Interactions](Inno.Editor.Interactions.md) 与各 Panel 页面。EditorScripts 必须显式 `using InnoEditor.*;`；项目完全禁止 global using。
