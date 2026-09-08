# Inno.Input

[Input 索引](README.md) · [Runtime](Inno.Input.Runtime.md) · [Wiki 首页](../README.md)

`Inno.Input` 是后端中立的物理输入契约。它引用 `Inno.Core.Input` 的 `KeyCode`、`MouseButton` 值类型，但不引用 SDL3、窗口实现、Editor 或具体 Input Action 模型。

## 公开 API

| API | 稳定语义 |
| --- | --- |
| `IInputBackend.Capture(frameIndex)` | 在帧边界产生完整不可变快照，并消费瞬时 pressed/released/delta。 |
| `IInputService.snapshot` | 当前 Session 当前帧的唯一输入状态。 |
| `InputSnapshot` | 键鼠 down/pressed/released、位置、delta、wheel 与 frame index。 |
| `InputExecutionContext` | `AsyncLocal` 隔离、严格 LIFO 的服务绑定。 |
| `Input` | 游戏脚本使用的无状态 façade。 |

```csharp
using InnoEngine.Input;

if (Input.WasKeyPressed(KeyCode.Space))
{
    // Trigger one gameplay command.
}
```

无活动 scope 时 façade 明确抛出异常。Snapshot 不受查询顺序影响；状态 owner 是 Runtime Subsystem，而不是静态类。Gamepad、text/IME 与 touch 尚未进入当前稳定契约。

[下一页：Inno.Input.Runtime](Inno.Input.Runtime.md)
