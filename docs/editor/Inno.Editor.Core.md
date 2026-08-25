# Inno.Editor.Core

[Editor 索引](README.md) · [Interactions](Inno.Editor.Interactions.md) · [Wiki 首页](../README.md)

`Inno.Editor.Core` 只保存 Editor 的被动生命周期契约。它不知道 Action、Menu、Selection、ImGui、Assets、Scene 或具体 Panel，因此可以被任意表现后端和 feature 安全引用。

## 目录与职责

```text
Inno.Editor.Core/
├─ Runtime/
│  ├─ EditorContext.cs
│  ├─ EditorFrame.cs
│  ├─ EditorRuntime.cs
│  ├─ EditorState.cs
│  └─ EditorLayoutSettings.cs
├─ Extensions/
│  ├─ EditorModule.cs
│  ├─ EditorModuleAttribute.cs
│  ├─ EditorPanel.cs
│  ├─ EditorPanelAttribute.cs
│  ├─ EditorModal.cs
│  ├─ EditorModalAttribute.cs
│  └─ IEditorPanelReloadState.cs
└─ Properties/ScriptingApi.cs
```

所有目录中的类型都使用物理 namespace `Inno.Editor.Core`；目录只表达职责，不扩展 namespace。Runtime 聚合项目上下文、帧、状态参数与 layout 实现，Extensions 聚合 Module/Panel/Modal 的发现契约，不再为每一种基类建立单独目录。

旧的 Commands、Menus、DragDrop、Selection 和 Rename 状态均不属于 Core，现已迁入 `Inno.Editor.Interactions` 或对应 Panel。

## Runtime API

| API | 说明 |
| --- | --- |
| `EditorContext` | 只读项目根目录、最新 `EditorFrame`，以及 `editor.ini` 的最小 layout façade；不提供 service locator。 |
| `EditorLayoutSettings` | internal 实现；协调 `editor.ini` 中互不覆盖的 ImGui layout 与可读具名 section。它不会成为跨项目公开依赖。 |
| `EditorFrame` | 一帧的 `deltaTime`、`totalTime`、`isFocused` 不可变快照。 |
| `EditorRuntime` | 表现无关的 `Start`、`Update(EditorFrame)`、`Dispose` 抽象。 |

`EditorContext` 是扩展共享的中立数据，不承担路由：

```csharp
var context = new EditorContext(projectDirectory);
Console.WriteLine(context.projectDirectory);
Console.WriteLine(context.layoutPath);
Console.WriteLine(context.imguiLayout);
```

构造函数把项目根规范化为绝对路径。Context 通过 `layoutPath`、`imguiLayout`、`GetLayoutSectionNames`、`TryGetLayoutSection`、`SetLayoutSection`、`RemoveLayoutSection`、`SetImGuiLayout`、`SaveLayoutIfChanged` 和 `SaveLayout` 提供明确的 layout 操作；具体文档类保持 internal。

这些 API 只处理 Module/Panel 项目状态与 Dear ImGui 使用的 `editor.ini`。业务设置由 [Inno.Editor.Settings](Inno.Editor.Settings.md) 独立写入项目根 `EditorSettings.json`。业务扩展若需要 Settings、Action/Menu/Selection，仍应在构造函数中接收 `EditorSettings` 或 `EditorInteractions`，而不是向 `EditorContext` 添加新服务属性。

## Module

`EditorModule` 表示跨 Panel 共享、随扩展 generation 启停的 feature 状态：

```csharp
[EditorModule("animation", order: 100)]
public sealed class AnimationModule : EditorModule
{
    private bool m_isBaking;

    public override bool blocksFollowingUpdates => m_isBaking;

    protected override void OnStart(EditorContext context)
    {
    }

    protected override void OnUpdate(EditorContext context)
    {
    }

    protected override void OnStop(EditorContext context)
    {
    }
}
```

`blocksFollowingUpdates` 是通用的原子启动/切换屏障：当前 Module 返回 `true` 时，排序在它之后的 Module 本帧不更新，但 Panel 和 Modal 仍可绘制。Scripting 用它保证脚本类型激活后 Scene 才恢复；未来 Shader/Pipeline bootstrap 也可以使用同一机制，避免业务模块互相引用。

`EditorModuleAttribute.id` 是必填且全局唯一的 Module identity。它同时用于发现冲突、诊断和可选状态 section；Module 不再声明第二个 workspace ID。

`EditorModule` 由扩展 Catalog 通过 `IDisposable` 统一释放，但 `Dispose` 是显式接口实现，不是派生类型的公开成员。Module 若拥有 Registry、watcher 或其他资源，只重写 `protected virtual OnDispose()`；Catalog 会在 Stop 且 generation 离开活动状态后调用一次。`IDisposable` 在这里仍有明确用途：它是 Catalog 与所有 Module 共用的基础设施 teardown 协议，而不是 feature 自己暴露或手工调用的生命周期 API。

Module、Panel、Action、Menu source 和 Drop handler 可以在唯一构造函数中请求 `EditorContext`、`EditorInteractions` 或一个无歧义的已发现 `EditorModule`。不存在手工注册表。

## Panel

```csharp
[EditorPanel("animation.graph", "Animator", order: 500, defaultOpen: true)]
public sealed class AnimatorPanel(AnimationModule animation) : EditorPanel
{
    public override void Draw(EditorContext context)
    {
        // Render the panel body.
    }
}
```

`EditorPanel.useWindowPadding` 默认返回 `true`，表示表现后端应使用标准窗口内边距。需要让背景、Tree 行或根滚动区域与 Dock body 边缘对齐的 Panel 可以重写为 `false`；正文仍可通过表现层的统一 content region 恢复恰好一层内边距。这是 Panel 的布局策略，不要求业务代码修改或重置滚动位置。

`EditorPanelAttribute.id` 必须稳定且全局唯一；它用于 Panel 菜单、窗口 identity 和 reload 状态。`title` 只用于显示，可以变化。

运行时始终按 ID 迁移 `isOpen`。需要迁移更多中立状态时实现 `IEditorPanelReloadState`，只返回不引用插件对象的字节：

```csharp
public ReadOnlyMemory<byte> CaptureReloadState() => m_stateBytes;

public void RestoreReloadState(ReadOnlyMemory<byte> state)
{
    m_stateBytes = state.ToArray();
}
```

## Modal

`EditorModal` 是被发现的阻塞或非阻塞浮层契约。具体位置、尺寸、淡入淡出和输入阻塞由 ImGui runtime 统一处理。

```csharp
using System.Numerics;

[EditorModal("animation.baking", "Baking Animation", order: 200)]
public sealed class AnimationBakeModal(AnimationModule animation) : EditorModal
{
    public override bool isVisible => animation.isBaking;
    public override bool blocksInteraction => true;
    public override bool canMove => true;
    public override bool canResize => true;
    public override Vector2 initialSize => new(900f, 600f);
    public override Vector2 minimumSize => new(640f, 420f);

    public override void Draw(EditorContext context)
    {
        // Draw body only; do not position the popup here.
    }
}
```

`canMove`、`canResize` 默认均为 false，因此既有进度 Modal 继续保持居中 auto-size。需要 Settings 风格窗口时可分别开启移动和缩放，并用 `initialSize` / `minimumSize` 提供未乘 zoom 的逻辑尺寸。Modal 仍是非 Dock 契约；具体 backend 必须阻止 Dock 与 Collapse/最小化。

## Scripting API

EditorScripts 使用唯一逻辑命名空间 `InnoEditor.Core`。它导出 Context、Frame、Runtime、Module、Panel、Modal、`EditorState` 和 Reload State 接口；不导出独立 Workspace interface、reader、writer 或 JSON DOM。脚本 Module/Panel 通过 protected `Capture(EditorState)` / `Restore(EditorState)` 选择持久化，所有脚本必须显式写普通 `using`。

## Module/Panel 项目状态

状态能力直接属于 `EditorModule` 与 `EditorPanel`。派生类型默认不保存任何状态；Catalog 只为真正 override `Capture(EditorState)` 的类型建立内部状态注册。没有 override Capture 的 Module/Panel 不会进入 restore、capture 或 section IO。唯一 ID 直接来自 `EditorModuleAttribute.id` 或 `EditorPanelAttribute.id`：

```csharp
[EditorModule("animation")]
public sealed class AnimationModule : EditorModule
{
    protected override void Capture(EditorState state)
    {
        state.Set("controller", m_controllerAssetId);
        state.Set("zoom", m_zoom);
    }

    protected override void Restore(EditorState state)
    {
        m_controllerAssetId = state.Get("controller", Guid.Empty);
        m_zoom = state.Get("zoom", 1f);
    }
}
```

`Restore` 只会为已 override Capture 的实例调用；section 不存在时，`state.Get` 直接返回调用者给出的 fallback。`EditorState` 是唯一公开参数契约，只提供 `Get` / `Set`；存储格式、JSON serializer 和 section 转换全部位于 Interactions 的 internal 实现中。Capture 参数可写，Restore 参数只读。状态只应保存项目相关、可重新解析的中立值，不保存 runtime 对象、线程、delegate 或插件实例，也不自行引入 schema 迁移字段。

`EditorSceneWorkspace` 名称中的 Workspace 仍表示“当前打开的 Scene 文档工作集”，不是另一种基础设施类型。它因为需要启动、逐帧同步和停止而是 `EditorModule`，并通过同一组 Capture/Restore hooks 额外保存 Scene 路径。

## 边界规则

- 不向 Core 添加 Rename、Open、Save、Asset 或 Scene 等 feature 概念。
- 不向 Context 添加 `IWhateverService` 集合或可变注册接口。
- 不在 Core 引用 ImGui。
- Action/Menu/Drag/Selection 统一见 [Interactions](Inno.Editor.Interactions.md)。
