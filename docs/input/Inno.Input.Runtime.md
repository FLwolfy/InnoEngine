# Inno.Input.Runtime

[Input 索引](README.md) · [Contract](Inno.Input.md) · [SDL3 Adapter](Inno.Adapter.Input.Sdl3.md)

`Inno.Input.Runtime` 把一个 `IInputBackend` 作为 Session-owned `RuntimeSubsystem`。`BeginFrame` 捕获一次状态并打开 `InputExecutionContext`；`EndFrame` 必定逆序关闭 scope；Detach/Dispose 会补偿未完整结束的帧并释放 backend。

## 公开 API

| API | 语义 |
| --- | --- |
| `InputRuntime` | 当前快照、frame scope 与 backend 所有者。 |
| `InputRuntimeFactory` | 为每个 Session 创建独立 backend/feature；可被重复用于 Edit、Play 与 Player。 |

Composition Root 只需把 factory 加入 `RuntimeSessionOptions.subsystemFactories`。测试可以注入 synthetic backend；Runtime 不依赖 SDL 或真实输入设备。创建失败会中止 Session 构造，释放已创建 Feature。

[下一页：Inno.Adapter.Input.Sdl3](Inno.Adapter.Input.Sdl3.md)
