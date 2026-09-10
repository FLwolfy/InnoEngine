# Inno.Shell

## 原生实时缩放与帧率

`framePacing` 是每 Shell 的 `FramePacingOptions`：`verticalSync` 控制设备同步，`maximumFrameRate = 0` 不施加软件上限。Editor 默认关闭 VSync；该策略不改变 fixed-step simulation。

Shell 订阅平台的 `redrawRequested`。系统模态缩放阻塞普通 PollEvent 循环时，使用同一个时钟、帧号及 Begin/Update/LateUpdate/Render/End 生命周期渲染，不递归 PumpEvents。重入保护避免正在执行的帧再次进入。原生 callback 不允许托管异常穿越 ABI；SDL adapter 将失败留到受控事件循环中重新抛出。

Presentation adapter 只能同步尺寸并请求绘制，不能独自回放 Editor draw callback。这一约束确保 Game/Scene 请求在同一帧提交和消费，BGFX 帧推进及资源退休仍由正常 runtime owner 完成。

## 有界产品退休

Shell 使用 Core 的 `RetirementBarrier`，在 owner thread 排空产品资源后才释放 rendering/input/window/platform。
同一 barrier 的 deadline 不随 Dispose 重试重置；超时仍属于未退休状态，不能将其聚合成一般错误后继续释放 adapter。
Editor 产品先通过 Play Mode loop quiesce，随后才进入 Module/Layer 资源栈的拆卸。

## 退出时的异步工作

Shell 在控制线程重试 product retirement，最多等待 30 秒。收到 `RetirementPendingException` 时不会销毁窗口、Input 或 GPU 依赖；超过等待期限仍将原屏障异常向上传递，不宣称成功。正常错误清理继续聚合，且 diagnostic producer 必须活到它服务的 owner 退休之后。

[Runtime 索引](README.md) · [Adapter contract](Inno.Adapter.md) · [架构 Overview](../architecture/ENGINE_ARCHITECTURE_OVERVIEW.md)

`Inno.Shell` 是 Player 与 Editor 共用的 backend-neutral Composition Host 基类，不是全局 singleton 或 Service Locator。

## 公开与 protected API

- `ShellOptions`：Adapter selection、window 和中立 rendering policy。
- `ShellFrame`：frame index、total time 与 delta time 的不可变值。
- `Shell`：拥有 application、primary window、input event source、render device 与公共 run loop。
- `InitializeAdapterResources`：派生产品完成非窗口 bootstrap 后显式创建公共 Adapter 资源。
- `Run` / `RequestExit`：执行一次事件与 frame 生命周期。
- `OnStarting`、`ShouldExit`、`OnEvent`、`OnFrame`、`OnSmokeCompleted`、`OnStopping`：产品行为 hook。
- `DisposeProductResources`：在公共 Adapter 逆序销毁前释放产品资源。

```csharp
internal sealed class GameHost : Shell
{
    internal GameHost(IAdapterCatalog adapters, ShellOptions options)
        : base(adapters, options)
    {
        InitializeAdapterResources();
    }

    protected override void OnFrame(ShellFrame frame)
    {
        // Advance the product runtime session.
    }
}
```

一个 Shell 只能运行一次。初始化中途失败会回滚已创建的 render/input/window/application；Dispose 先调用产品释放，再按 render → input → window → platform 逆序清理，并聚合 cleanup exception。`GamePlayerHost` 与 `EditorHost` 都必须继承 Shell，Architecture Tool 会拒绝直接引用具体 adapter 的 Host。
