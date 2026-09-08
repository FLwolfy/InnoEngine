# Inno.Core.Layers

[Core 索引](README.md) · [Wiki 首页](../README.md) · [Runtime](../runtime/README.md) · [架构 Overview](../architecture/ENGINE_ARCHITECTURE_OVERVIEW.md)

## 职责与边界

`Inno.Core.Layers` 提供不依赖 Editor、Player、Scene、Rendering 或任何 native backend 的有序生命周期原语。
它解决三件事：Layer/Overlay 顺序、每个 Layer 独立的 `EventHub` 作用域，以及 render prepare/submit/unwind
的确定性执行。它不创建窗口、设备、RuntimeSession 或业务服务，也不是 Composition Shell。

旧 `Inno.Core.Framework` 同时拥有 Shell、Assets、Plugins 与 Settings，依赖方向错误，因此不能恢复。
其中真正通用的 `Layer`/`LayerStack` 被提取为本项目；`Inno.Shell` 和产品 Host 仍留在 Composition。

项目依赖 `Inno.Core.Events` 与 `Inno.Core.Execution`，不导出 Scripting API。
Execution 只提供统一 Pending 退休信号；Stack 不创建自己的 generation gate 或设备。

## 公开 API

| API | 稳定语义 |
| --- | --- |
| `Layer` | 可派生的生命周期参与者；订阅在每次 attachment 内自动释放 |
| `Layer.name` | 诊断名称，不作为持久 ID 或类型 ID |
| `OnAttach` / `OnDetach` | 进入和离开一个 LayerStack attachment |
| `OnFixedUpdate` / `OnUpdate` / `OnLateUpdate` | 固定步、普通帧和迟更新阶段 |
| `OnBeforeRender` / `OnRender` / `OnAfterRender` | 准备、提交和逆序释放 render frame ownership |
| `Listen<T>` / `ListenOnce<T>` | 在当前 Layer 的独立 EventHub 中订阅，Detach 时自动释放 |
| `Announce` | 只向当前 Layer 自身 EventHub 同步分发 |
| `LayerStack` | 拥有 Base Layer 与 Overlay 的顺序、事件 scope 和 attachment lifetime |
| `PushLayer` / `PopLayer` | 管理 Overlay 下方的 base region |
| `PushOverlay` / `PopOverlay` | 管理 stack 顶部的 overlay region |
| `Clear` / `Dispose` | 逆序 Detach；`Clear` 后可复用，`Dispose` 后永久关闭 |

## 顺序模型

- Base Layer 总在 Overlay 之前；后加入 Base Layer 会插在现有 Overlay 下方。
- Update、LateUpdate、Render preparation 和 Render submission 从底到顶执行。
- `OnAfterRender` 只对成功完成 `OnBeforeRender` 的 Layer 执行，并从顶到底 unwind。
- 每个 Layer 使用独立 `EventHub`；顶层 Layer 的 hub order 更高，因此跨 Hub event 优先到达 Overlay。
- 同一个 Layer 实例不能重复加入一个 Stack；Layer 也不能同时持有两个 attachment。

## 常见工作流

```csharp
using System;

using Inno.Core.Events;
using Inno.Core.Layers;

var dispatcher = new EventDispatcher();
using var layers = new LayerStack(() => dispatcher.CreateHub());
layers.PushOverlay(new ConsoleOverlay());
layers.OnUpdate(1f / 60f);
layers.RenderFrame(1f / 60f);

public sealed class ConsoleOverlay : Layer
{
    public ConsoleOverlay()
        : base("Console")
    {
    }

    public override void OnAttach()
    {
        _ = Listen<ApplicationQuitEvent>(OnQuitRequested);
    }

    public override void OnUpdate(float deltaTime)
    {
        // Update backend-neutral overlay state.
    }

    private static void OnQuitRequested(ApplicationQuitEvent evnt)
    {
        evnt.HandleInHub();
    }
}
```

## 错误与生命周期

- `OnAttach` 失败时，已登记 entry 仍由 Stack 拥有，并执行 `OnDetach` 补偿部分启动。补偿完成后才移除 entry 和 EventHub。
- `RetirementPendingException` 表示依赖仍在使用：保留当前 entry、EventHub 和所有下层资源，拒绝 Push/Pop/帧调用；通过 `Clear` 或 `Dispose` 继续退休。Stack 本身不阻塞等待，最外层 Host 使用统一 `RetirementBarrier` 提供 deadline。
- Detach 开始先注销事件订阅，避免退出中的 Layer 再接收事件。`OnDetach` 的已完成步骤须可重试，Pending 不清除 attachment owner。
- 普通清理异常表示本步骤已结束但失败，后续资源仍逆序尝试；此前的错误跨 Pending 保留，最终统一报告，不重复退休已移除的 entry。
- Render callback 失败后仍会逆序执行所有已完成 prepare 的 `OnAfterRender`。
- Dispose 完成后幂等；完成前 Pending 不能标为 disposed。Dispose 后调用 mutation 或 frame API 会抛出 `ObjectDisposedException`。Clear 完整退出后才恢复可复用状态。
- Layer 不跨 Plugin generation 保存 runtime object；需要跨代恢复的业务状态仍应使用 Stable ID 与中立 bytes。

## 与 Shell、Runtime Subsystem 的关系

`LayerStack` 是通用的局部顺序容器。`Inno.Shell` 是 application-level Host 基类；`IRuntimeSubsystem` 是
RuntimeSession 的正式 feature pipeline。三者不能互相替代：Editor 可以在 Shell 的 `OnFrame` 中使用
LayerStack 排列 presentation layer，但 Runtime feature 的 attach/safe-point/detach 仍由 RuntimeSession 管理。
