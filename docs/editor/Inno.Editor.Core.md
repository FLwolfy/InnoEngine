# Inno.Editor.Core

[Editor 索引](README.md) · [Interactions](Inno.Editor.Interactions.md) · [Wiki 首页](../README.md)

`Inno.Editor.Core` 只保存 Editor 的被动生命周期契约。它不知道 Action、Menu、Selection、ImGui、Assets、Scene 或具体 Panel，因此可以被任意表现后端和 feature 安全引用。

## 目录与职责

```text
Inno.Editor.Core/
├─ Runtime/
│  ├─ EditorContext.cs
│  ├─ EditorFrame.cs
│  └─ EditorRuntime.cs
├─ Panels/
│  ├─ EditorPanel.cs
│  ├─ EditorModal.cs
│  └─ IEditorPanelReloadState.cs
├─ EditorModule.cs
└─ Properties/ScriptingApi.cs
```

旧的 Commands、Menus、DragDrop、Selection 和 Rename 状态均不属于 Core，现已迁入 `Inno.Editor.Interactions` 或对应 Panel。

## Runtime API

| API | 说明 |
| --- | --- |
| `EditorContext` | 只读项目根目录和最新 `EditorFrame`；不提供 service locator。 |
| `EditorFrame` | 一帧的 `deltaTime`、`totalTime`、`isFocused` 不可变快照。 |
| `EditorRuntime` | 表现无关的 `Start`、`Update(EditorFrame)`、`Dispose` 抽象。 |

`EditorContext` 是扩展共享的中立数据，不承担路由：

```csharp
var context = new EditorContext(projectDirectory);
Console.WriteLine(context.projectDirectory);
```

业务扩展若需要 Action/Menu/Selection，应由构造函数接收 `EditorInteractions`，而不是向 `EditorContext` 添加新服务属性。

## Module

`EditorModule` 表示跨 Panel 共享、随扩展 generation 启停的 feature 状态：

```csharp
[EditorModule(order: 100)]
public sealed class AnimationModule : EditorModule
{
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

`EditorPanelAttribute.id` 必须稳定且全局唯一；它用于 View 菜单、窗口 identity 和 reload 状态。`title` 只用于显示，可以变化。

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
[EditorModal("animation.baking", "Baking Animation", order: 200)]
public sealed class AnimationBakeModal(AnimationModule animation) : EditorModal
{
    public override bool isVisible => animation.isBaking;
    public override bool blocksInteraction => true;

    public override void Draw(EditorContext context)
    {
        // Draw body only; do not position the popup here.
    }
}
```

## Scripting API

EditorScripts 使用逻辑命名空间 `InnoEditor.Core` 和 `InnoEditor.Panels`。只导出 Context、Frame、Runtime、Module、Panel、Modal 及其 Attribute/状态接口；所有脚本必须显式写普通 `using`。

## 边界规则

- 不向 Core 添加 Rename、Open、Save、Asset 或 Scene 等 feature 概念。
- 不向 Context 添加 `IWhateverService` 集合或可变注册接口。
- 不在 Core 引用 ImGui。
- Action/Menu/Drag/Selection 统一见 [Interactions](Inno.Editor.Interactions.md)。
